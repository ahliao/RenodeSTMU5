//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class STM32U5_RCC : BasicDoubleWordPeripheral, IKnownSize
    {
        public STM32U5_RCC(IMachine machine, IHasFrequency nvic = null, IHasFrequency lptim1 = null, IHasFrequency lptim2 = null,
            IHasFrequency lptim3 = null, IHasFrequency lptim4 = null, ulong lsiFrequency = DefaultLsiFrequency,
            ulong lseFrequency = DefaultLseFrequency, ulong hseFrequency = DefaultHseFrequency, ulong msikFrequency = DefaultMsikFrequency) : base(machine)
        {
            this.nvic = nvic;
            this.lptim1 = lptim1;
            this.lptim2 = lptim2;
            this.lptim3 = lptim3;
            this.lptim4 = lptim4;
            this.lsiFrequency = lsiFrequency;
            this.lseFrequency = lseFrequency;
            this.hseFrequency = hseFrequency;
            this.msikFrequency = msikFrequency;

            DefineRegisters();
            Reset();
        }

        public override void Reset()
        {
            base.Reset();
            UpdateClocks();
        }

        public long Size => 0x400;

        private static void TrySetFrequency(IHasFrequency clockedPeripheral, ulong frequency)
        {
            if(clockedPeripheral != null)
            {
                clockedPeripheral.Frequency = frequency;
            }
        }

        private void DefineRegisters()
        {
            Registers.ClockControl.Define(this, 0x35)
                .WithFlag(0, out var msisEnable, name: "MSISON")
                .WithFlag(1, name: "MSIKERON")
                .WithFlag(2, FieldMode.Read, valueProviderCallback: _ => msisEnable.Value, name: "MSISRDY")
                .WithFlag(3, name: "MSIPLLEN")
                .WithFlag(4, out var msikEnable, name: "MSIKON")
                .WithFlag(5, FieldMode.Read, valueProviderCallback: _ => msikEnable.Value, name: "MSIKRDY")
                .WithTaggedFlag("MSIPLLSEL", 6)
                .WithTaggedFlag("MSIPLLFAST", 7)
                .WithFlag(8, out var hsi16Enable, name: "HSION")
                .WithTaggedFlag("HSIKERON", 9)
                .WithFlag(10, FieldMode.Read, valueProviderCallback: _ => hsi16Enable.Value, name: "HSIRDY")
                .WithReservedBits(11, 1)
                .WithFlag(12, out var hsi48Enable, name: "HSI48ON")
                .WithFlag(13, FieldMode.Read, valueProviderCallback: _ => hsi48Enable.Value, name: "HSI48RDY")
                .WithReservedBits(14, 2)
                .WithFlag(16, out var hseEnable, name: "HSEON")
                .WithFlag(17, FieldMode.Read, valueProviderCallback: _ => hseEnable.Value, name: "HSERDY")
                .WithReservedBits(18, 1)
                .WithTaggedFlag("HSECSSON", 19)
                .WithFlag(20, out hsePrescaler, name: "HSEPRE")
                .WithReservedBits(21, 3)
                .WithFlag(24, out var pll1Enable, name: "PLL1ON")
                .WithFlag(25, FieldMode.Read, valueProviderCallback: _ => pll1Enable.Value, name: "PLL1RDY")
                .WithFlag(26, out var pll2Enable, name: "PLL2ON")
                .WithFlag(27, FieldMode.Read, valueProviderCallback: _ => pll2Enable.Value, name: "PLL2RDY")
                .WithFlag(28, out var pll3Enable, name: "PLL3ON")
                .WithFlag(29, FieldMode.Read, valueProviderCallback: _ => pll3Enable.Value, name: "PLL3RDY")
                .WithReservedBits(30, 2)
                .WithChangeCallback((_, __) => UpdateClocks());

            Registers.ClockConfiguration1.Define(this)
                .WithEnumField(0, 2, out systemClockSwitch, name: "SW")
                .WithEnumField<DoubleWordRegister, SystemClockSource>(2, 2, FieldMode.Read, valueProviderCallback: _ => systemClockSwitch.Value, name: "SWS")
                .WithTaggedFlag("STOPWUCK", 4)
                .WithTaggedFlag("STOPKERWUCK", 5)
                .WithReservedBits(6, 18)
                .WithTag("MCOSEL", 24, 4)
                .WithTag("MCOPRE", 28, 3)
                .WithReservedBits(31, 1)
                .WithChangeCallback((_, __) => UpdateClocks());

            Registers.ClockConfiguration2.Define(this, 0x6000)
                .WithValueField(0, 4, out ahbPrescaler, name: "HPRE")
                .WithValueField(4, 3, out apb1Prescaler, name: "PPRE1")
                .WithValueField(8, 3, out apb2Prescaler, name: "PPRE2")
                .WithValueField(12, 3, out apb3Prescaler, name: "PPRE3")
                .WithReservedBits(15, 17)
                .WithChangeCallback((_, __) => UpdateClocks());

            Registers.ClockInterruptEnable.Define(this)
                .WithFlag(0, out lsiReadyInterruptEnable, name: "LSIRDYIE")
                .WithFlag(1, out lseReadyInterruptEnable, name: "LSERDYIE")
                .WithFlag(2, name: "MSIRDYIE")
                .WithFlag(3, name: "HSIRDYIE")
                .WithFlag(4, name: "HSERDYIE")
                .WithFlag(5, name: "HSI48RDYIE")
                .WithFlag(6, name: "PLL1RDYIE")
                .WithFlag(7, name: "PLL2RDYIE")
                .WithFlag(8, name: "PLL3RDYIE")
                .WithReservedBits(9, 23);

            Registers.ClockInterruptFlag.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => lsiReadyInterruptFlag, name: "LSIRDYF")
                .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => lseReadyInterruptFlag, name: "LSERDYF")
                .WithReservedBits(2, 1)
                .WithFlag(3, FieldMode.Read, valueProviderCallback: _ => false, name: "HSIRDYF")
                .WithFlag(4, FieldMode.Read, valueProviderCallback: _ => false, name: "HSERDYF")
                .WithFlag(5, FieldMode.Read, valueProviderCallback: _ => false, name: "HSI48RDYF")
                .WithFlag(6, FieldMode.Read, valueProviderCallback: _ => false, name: "PLL1RDYF")
                .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => false, name: "PLL2RDYF")
                .WithFlag(8, FieldMode.Read, valueProviderCallback: _ => false, name: "PLL3RDYF")
                .WithReservedBits(9, 1)
                .WithFlag(10, FieldMode.Read, valueProviderCallback: _ => false, name: "HSECSSF")
                .WithReservedBits(11, 21);

            Registers.ClockInterruptClear.Define(this)
                .WithFlag(0, FieldMode.WriteOneToClear, writeCallback: (_, value) => { if(value) { lsiReadyInterruptFlag = false; } }, name: "LSIRDYC")
                .WithFlag(1, FieldMode.WriteOneToClear, writeCallback: (_, value) => { if(value) { lseReadyInterruptFlag = false; } }, name: "LSERDYC")
                .WithReservedBits(2, 1)
                .WithFlag(3, FieldMode.Write, name: "HSIRDYC")
                .WithFlag(4, FieldMode.Write, name: "HSERDYC")
                .WithFlag(5, FieldMode.Write, name: "HSI48RDYC")
                .WithFlag(6, FieldMode.Write, name: "PLL1RDYC")
                .WithFlag(7, FieldMode.Write, name: "PLL2RDYC")
                .WithFlag(8, FieldMode.Write, name: "PLL3RDYC")
                .WithReservedBits(9, 1)
                .WithFlag(10, FieldMode.Write, name: "HSECSSC")
                .WithReservedBits(11, 21);

            Registers.Apb1PeripheralReset1.Define(this)
                .WithReservedBits(0, 5)
                .WithFlag(5, FieldMode.Write, writeCallback: (_, value) => ResetPeripheral(value, lptim2), name: "LPTIM2RST")
                .WithReservedBits(6, 26);

            Registers.Apb3PeripheralReset.Define(this)
                .WithReservedBits(0, 11)
                .WithFlag(11, FieldMode.Write, writeCallback: (_, value) => ResetPeripheral(value, lptim1), name: "LPTIM1RST")
                .WithFlag(12, FieldMode.Write, writeCallback: (_, value) => ResetPeripheral(value, lptim3), name: "LPTIM3RST")
                .WithFlag(13, FieldMode.Write, writeCallback: (_, value) => ResetPeripheral(value, lptim4), name: "LPTIM4RST")
                .WithReservedBits(14, 18);

            Registers.Apb1PeripheralEnable1.Define(this)
                .WithReservedBits(0, 5)
                .WithFlag(5, name: "LPTIM2EN")
                .WithReservedBits(6, 26);

            Registers.Apb3PeripheralEnable.Define(this)
                .WithReservedBits(0, 11)
                .WithFlag(11, name: "LPTIM1EN")
                .WithFlag(12, name: "LPTIM3EN")
                .WithFlag(13, name: "LPTIM4EN")
                .WithReservedBits(14, 18);

            Registers.Apb1PeripheralSleepEnable1.Define(this, 0xFFFFFFFF)
                .WithReservedBits(0, 5)
                .WithFlag(5, name: "LPTIM2SMEN")
                .WithReservedBits(6, 26);

            Registers.Apb3PeripheralSleepEnable.Define(this, 0xFFFFFFFF)
                .WithReservedBits(0, 11)
                .WithFlag(11, name: "LPTIM1SMEN")
                .WithFlag(12, name: "LPTIM3SMEN")
                .WithFlag(13, name: "LPTIM4SMEN")
                .WithReservedBits(14, 18);

            Registers.PeripheralsIndependentClockConfiguration1.Define(this)
                .WithTag("USART1SEL", 0, 2)
                .WithTag("USART2SEL", 2, 2)
                .WithTag("USART3SEL", 4, 2)
                .WithTag("UART4SEL", 6, 2)
                .WithTag("UART5SEL", 8, 2)
                .WithTag("I2C1SEL", 10, 2)
                .WithTag("I2C2SEL", 12, 2)
                .WithTag("I2C4SEL", 14, 2)
                .WithTag("SPI2SEL", 16, 2)
                .WithEnumField(18, 2, out lpTimer2Clock, name: "LPTIM2SEL")
                .WithTag("SPI1SEL", 20, 2)
                .WithTag("SYSTICKSEL", 22, 2)
                .WithTag("FDCAN1SEL", 24, 2)
                .WithTag("ICLKSEL", 26, 2)
                .WithReservedBits(28, 1)
                .WithTag("TIMICSEL", 29, 3)
                .WithChangeCallback((_, __) => UpdateClocks());

            Registers.PeripheralsIndependentClockConfiguration3.Define(this, LpTimer1LsiClockSourceResetValue)
                .WithTag("LPUART1SEL", 0, 3)
                .WithTag("SPI3SEL", 3, 2)
                .WithReservedBits(5, 1)
                .WithTag("I2C3SEL", 6, 2)
                .WithEnumField(8, 2, out lpTimer34Clock, name: "LPTIM34SEL")
                .WithEnumField(10, 2, out lpTimer1Clock, name: "LPTIM1SEL")
                .WithTag("ADCDACSEL", 12, 3)
                .WithReservedBits(15, 17)
                .WithChangeCallback((_, __) => UpdateClocks());

            Registers.BackupDomainControl.Define(this)
                .WithFlag(0, out lseEnable, name: "LSEON")
                .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => lseEnable.Value, name: "LSERDY")
                .WithTaggedFlag("LSEBYP", 2)
                .WithTag("LSEDRV", 3, 2)
                .WithTaggedFlag("LSECSSON", 5)
                .WithFlag(6, FieldMode.Read, valueProviderCallback: _ => false, name: "LSECSSD")
                .WithFlag(7, out var lseSystemClockEnable, name: "LSESYSEN")
                .WithTag("RTCSEL", 8, 2)
                .WithReservedBits(10, 1)
                .WithFlag(11, FieldMode.Read, valueProviderCallback: _ => lseSystemClockEnable.Value && lseEnable.Value, name: "LSESYSRDY")
                .WithTaggedFlag("LSEGFON", 12)
                .WithReservedBits(13, 2)
                .WithTaggedFlag("RTCEN", 15)
                .WithTaggedFlag("BDRST", 16)
                .WithReservedBits(17, 7)
                .WithTaggedFlag("LSCOEN", 24)
                .WithTaggedFlag("LSCOSEL", 25)
                .WithFlag(26, out lsiEnable, name: "LSION")
                .WithFlag(27, FieldMode.Read, valueProviderCallback: _ => lsiEnable.Value, name: "LSIRDY")
                .WithTaggedFlag("LSIPREDIV", 28)
                .WithReservedBits(29, 3)
                .WithChangeCallback((_, __) =>
                {
                    if(lsiEnable.Value && lsiReadyInterruptEnable.Value)
                    {
                        lsiReadyInterruptFlag = true;
                    }
                    if(lseEnable.Value && lseReadyInterruptEnable.Value)
                    {
                        lseReadyInterruptFlag = true;
                    }
                    UpdateClocks();
                });

            Registers.ControlStatus.Define(this, 0x0C004400)
                .WithReservedBits(0, 8)
                .WithTag("MSISRANGE", 8, 4)
                .WithTag("MSIKRANGE", 12, 4)
                .WithReservedBits(16, 7)
                .WithTaggedFlag("RMVF", 23)
                .WithReservedBits(24, 1)
                .WithTaggedFlag("OBLRSTF", 25)
                .WithTaggedFlag("PINRSTF", 26)
                .WithTaggedFlag("BORRSTF", 27)
                .WithTaggedFlag("SFTRSTF", 28)
                .WithTaggedFlag("IWDGRSTF", 29)
                .WithTaggedFlag("WWDGRSTF", 30)
                .WithTaggedFlag("LPWRRSTF", 31);
        }

        private void ResetPeripheral(bool value, IHasFrequency peripheral)
        {
            if(value)
            {
                (peripheral as IPeripheral)?.Reset();
            }
        }

        private void UpdateClocks()
        {
            TrySetFrequency(nvic, SystemClock);
            TrySetFrequency(lptim1, GetLpTimerClock(lpTimer1Clock.Value, msikFrequency));
            TrySetFrequency(lptim2, GetLpTimerClock(lpTimer2Clock.Value, Apb1Clock));
            TrySetFrequency(lptim3, GetLpTimerClock(lpTimer34Clock.Value, msikFrequency));
            TrySetFrequency(lptim4, GetLpTimerClock(lpTimer34Clock.Value, msikFrequency));
        }

        private ulong GetLpTimerClock(LpTimerClockSource source, ulong defaultClock)
        {
            switch(source)
            {
            case LpTimerClockSource.Default:
                return defaultClock;
            case LpTimerClockSource.Lsi:
                return lsiFrequency;
            case LpTimerClockSource.Hsi16:
                return Hsi16Frequency;
            case LpTimerClockSource.Lse:
                return lseFrequency;
            default:
                throw new ArgumentException("Unreachable: Invalid LPTIM clock source");
            }
        }

        private ulong PrescaleApbAhb(ulong input, IValueRegisterField prescaler)
        {
            var ppre = (int)prescaler.Value;
            if((ppre & 4) == 0)
            {
                return input;
            }
            var logDivisor = (ppre & 3) + 1;
            return input >> logDivisor;
        }

        private ulong SystemClock
        {
            get
            {
                switch(systemClockSwitch.Value)
                {
                default:
                case SystemClockSource.Msis:
                case SystemClockSource.Msik:
                    return msikFrequency;
                case SystemClockSource.Hsi16:
                    return Hsi16Frequency;
                case SystemClockSource.Hse:
                    return DividedHse;
                }
            }
        }

        private ulong DividedHse => hseFrequency / (hsePrescaler.Value ? 2UL : 1UL);

        private ulong AhbClock => PrescaleApbAhb(SystemClock, ahbPrescaler);

        private ulong Apb1Clock => PrescaleApbAhb(AhbClock, apb1Prescaler);

        private IFlagRegisterField hsePrescaler;
        private IFlagRegisterField lseEnable;
        private IFlagRegisterField lseReadyInterruptEnable;
        private IFlagRegisterField lsiEnable;
        private IFlagRegisterField lsiReadyInterruptEnable;
        private IEnumRegisterField<SystemClockSource> systemClockSwitch;
        private IEnumRegisterField<LpTimerClockSource> lpTimer1Clock;
        private IEnumRegisterField<LpTimerClockSource> lpTimer2Clock;
        private IEnumRegisterField<LpTimerClockSource> lpTimer34Clock;
        private IValueRegisterField ahbPrescaler;
        private IValueRegisterField apb1Prescaler;
        private IValueRegisterField apb2Prescaler;
        private IValueRegisterField apb3Prescaler;

        private bool lseReadyInterruptFlag;
        private bool lsiReadyInterruptFlag;

        private readonly IHasFrequency nvic;
        private readonly IHasFrequency lptim1;
        private readonly IHasFrequency lptim2;
        private readonly IHasFrequency lptim3;
        private readonly IHasFrequency lptim4;
        private readonly ulong lsiFrequency;
        private readonly ulong lseFrequency;
        private readonly ulong hseFrequency;
        private readonly ulong msikFrequency;

        private const ulong DefaultLsiFrequency = 32000;
        private const uint LpTimer1LsiClockSourceResetValue = 0x400;
        private const ulong DefaultLseFrequency = 32768;
        private const ulong DefaultHseFrequency = 16000000;
        private const ulong DefaultMsikFrequency = 16000000;
        private const ulong Hsi16Frequency = 16000000;

        private enum SystemClockSource
        {
            Msis = 0,
            Hsi16 = 1,
            Hse = 2,
            Msik = 3,
        }

        private enum LpTimerClockSource
        {
            Default = 0,
            Lsi = 1,
            Hsi16 = 2,
            Lse = 3,
        }

        private enum Registers : long
        {
            ClockControl = 0x00,
            ClockConfiguration1 = 0x1C,
            ClockConfiguration2 = 0x20,
            ClockInterruptEnable = 0x50,
            ClockInterruptFlag = 0x54,
            ClockInterruptClear = 0x58,
            Apb1PeripheralReset1 = 0x78,
            Apb3PeripheralReset = 0x80,
            Apb1PeripheralEnable1 = 0xA0,
            Apb3PeripheralEnable = 0xA8,
            Apb1PeripheralSleepEnable1 = 0xC8,
            Apb3PeripheralSleepEnable = 0xD0,
            PeripheralsIndependentClockConfiguration1 = 0xE0,
            PeripheralsIndependentClockConfiguration3 = 0xE8,
            BackupDomainControl = 0xF0,
            ControlStatus = 0xF4,
        }
    }
}
