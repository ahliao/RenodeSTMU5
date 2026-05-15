//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class STM32U5_LpTimer : LimitTimer, IDoubleWordPeripheral, IKnownSize
    {
        public STM32U5_LpTimer(IMachine machine, ulong frequency) : base(machine.ClockSource, frequency, limit: 0x1, direction: Direction.Ascending, enabled: false, eventEnabled: true)
        {
            IRQ = new GPIO();

            compareTimers = new LimitTimer[CompareChannelsCount];
            for(var i = 0; i < CompareChannelsCount; i++)
            {
                var channel = i;
                compareTimers[channel] = new LimitTimer(machine.ClockSource, frequency, this, $"compareTimer{channel + 1}", 0x1, Direction.Ascending, workMode: WorkMode.OneShot, eventEnabled: true, autoUpdate: true);
                compareTimers[channel].LimitReached += () =>
                {
                    this.Log(LogLevel.Debug, "Compare {0} reached", channel + 1);
                    compareTimers[channel].Enabled = false;
                    captureCompareInterruptStatus[channel].Value = true;
                    UpdateInterrupts();
                };
            }

            LimitReached += () =>
            {
                this.Log(LogLevel.Debug, "AutoReload reached");
                autoReloadMatchInterruptStatus.Value = true;
                if(Mode == WorkMode.Periodic)
                {
                    UpdateCompareTimers();
                }
                UpdateInterrupts();
            };

            var singleStartRequested = false;
            var continuousStartRequested = false;

            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.InterruptAndStatus, new DoubleWordRegister(this)
                    .WithFlag(0, out captureCompareInterruptStatus[0], FieldMode.Read, name: "Capture/compare 1 interrupt flag (CC1IF)")
                    .WithFlag(1, out autoReloadMatchInterruptStatus, FieldMode.Read, name: "Autoreload match (ARRM)")
                    .WithTaggedFlag("External trigger edge event (EXTTRIG)", 2)
                    .WithFlag(3, out compareRegisterUpdateOkStatus[0], FieldMode.Read, name: "Compare register 1 update OK (CMP1OK)")
                    .WithFlag(4, out autoReloadRegisterUpdateOkStatus, FieldMode.Read, name: "Autoreload register update OK (ARROK)")
                    .WithTaggedFlag("Counter direction change down to up (UP)", 5)
                    .WithTaggedFlag("Counter direction change up to down (DOWN)", 6)
                    .WithFlag(7, out updateEventInterruptStatus, FieldMode.Read, name: "Update event occurred (UE)")
                    .WithFlag(8, out repetitionUpdateOkStatus, FieldMode.Read, name: "Repetition register update OK (REPOK)")
                    .WithFlag(9, out captureCompareInterruptStatus[1], FieldMode.Read, name: "Capture/compare 2 interrupt flag (CC2IF)")
                    .WithReservedBits(10, 9)
                    .WithFlag(19, out compareRegisterUpdateOkStatus[1], FieldMode.Read, name: "Compare register 2 update OK (CMP2OK)")
                    .WithReservedBits(20, 4)
                    .WithFlag(24, out interruptEnableRegisterUpdateOkStatus, FieldMode.Read, name: "Interrupt enable register update OK (DIEROK)")
                    .WithReservedBits(25, 7)
                },
                {(long)Registers.InterruptClear, new DoubleWordRegister(this)
                    .WithFlag(0, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, captureCompareInterruptStatus[0]), name: "Capture/compare 1 clear flag (CC1CF)")
                    .WithFlag(1, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, autoReloadMatchInterruptStatus), name: "Autoreload match clear flag (ARRMCF)")
                    .WithTaggedFlag("External trigger valid edge clear flag (EXTTRIGCF)", 2)
                    .WithFlag(3, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, compareRegisterUpdateOkStatus[0]), name: "Compare register 1 update OK clear flag (CMP1OKCF)")
                    .WithFlag(4, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, autoReloadRegisterUpdateOkStatus), name: "Autoreload register update OK clear flag (ARROKCF)")
                    .WithTaggedFlag("Counter direction change down to up clear flag (UPCF)", 5)
                    .WithTaggedFlag("Counter direction change up to down clear flag (DOWNCF)", 6)
                    .WithFlag(7, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, updateEventInterruptStatus), name: "Update event clear flag (UECF)")
                    .WithFlag(8, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, repetitionUpdateOkStatus), name: "Repetition register update OK clear flag (REPOKCF)")
                    .WithFlag(9, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, captureCompareInterruptStatus[1]), name: "Capture/compare 2 clear flag (CC2CF)")
                    .WithReservedBits(10, 9)
                    .WithFlag(19, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, compareRegisterUpdateOkStatus[1]), name: "Compare register 2 update OK clear flag (CMP2OKCF)")
                    .WithReservedBits(20, 4)
                    .WithFlag(24, FieldMode.WriteOneToClear, writeCallback: (_, value) => ClearFlag(value, interruptEnableRegisterUpdateOkStatus), name: "Interrupt enable register update OK clear flag (DIEROKCF)")
                    .WithReservedBits(25, 7)
                    .WithWriteCallback((_, __) => UpdateInterrupts())
                },
                {(long)Registers.InterruptEnable, new DoubleWordRegister(this)
                    .WithFlag(0, out captureCompareInterruptEnable[0], name: "Capture/compare 1 interrupt enable (CC1IE)")
                    .WithFlag(1, out autoReloadMatchInterruptEnable, name: "Autoreload match interrupt enable (ARRMIE)")
                    .WithTaggedFlag("External trigger edge event interrupt enable (EXTTRIGIE)", 2)
                    .WithFlag(3, out compareRegisterUpdateOkEnable[0], name: "Compare register 1 update OK interrupt enable (CMP1OKIE)")
                    .WithFlag(4, out autoReloadRegisterUpdateOkEnable, name: "Autoreload register update OK interrupt enable (ARROKIE)")
                    .WithFlag(5, name: "Counter direction change down to up interrupt enable (UPIE)")
                    .WithFlag(6, name: "Counter direction change up to down interrupt enable (DOWNIE)")
                    .WithFlag(7, out updateEventInterruptEnable, name: "Update event interrupt enable (UEIE)")
                    .WithFlag(8, out repetitionUpdateOkEnable, name: "Repetition register update OK interrupt enable (REPOKIE)")
                    .WithFlag(9, out captureCompareInterruptEnable[1], name: "Capture/compare 2 interrupt enable (CC2IE)")
                    .WithReservedBits(10, 9)
                    .WithFlag(19, out compareRegisterUpdateOkEnable[1], name: "Compare register 2 update OK interrupt enable (CMP2OKIE)")
                    .WithReservedBits(20, 12)
                    .WithWriteCallback((_, __) =>
                    {
                        interruptEnableRegisterUpdateOkStatus.Value = true;
                        UpdateInterrupts();
                    })
                },
                {(long)Registers.Configuration, new DoubleWordRegister(this)
                    .WithTaggedFlag("Clock selector (CKSEL)", 0)
                    .WithTag("Clock Polarity (CKPOL)", 1, 2)
                    .WithTag("Configurable digital filter for external clock (CKFLT)", 3, 2)
                    .WithReservedBits(5, 1)
                    .WithTag("Configurable digital filter for trigger (TRGFLT)", 6, 2)
                    .WithReservedBits(8, 1)
                    .WithValueField(9, 3,
                        writeCallback: (_, value) =>
                        {
                            var divider = 1UL << (int)value;
                            Divider = divider;
                            for(var i = 0; i < CompareChannelsCount; i++)
                            {
                                compareTimers[i].Divider = divider;
                            }
                        },
                        valueProviderCallback: _ => (uint)Math.Log(Divider, 2),
                        name: "Clock prescaler (PRESC)")
                    .WithReservedBits(12, 1)
                    .WithTag("Trigger Selector (TRIGSEL)", 13, 3)
                    .WithReservedBits(16, 1)
                    .WithTag("Trigger Enable and Polarity (TRIGEN)", 17, 2)
                    .WithTaggedFlag("Timeout enable (TIMOUT)", 19)
                    .WithTaggedFlag("Waveform Shape (WAVE)", 20)
                    .WithTaggedFlag("Waveform shape polarity (WAVPOL)", 21)
                    .WithTaggedFlag("Registers update mode (PRELOAD)", 22)
                    .WithTaggedFlag("Counter mode enabled (COUNTMODE)", 23)
                    .WithTaggedFlag("Encoder mode enable (ENC)", 24)
                    .WithReservedBits(25, 7)
                },
                {(long)Registers.Control, new DoubleWordRegister(this)
                    .WithFlag(0, out var enabled, name: "LPTIM enable (ENABLE)")
                    .WithFlag(1, FieldMode.Write, writeCallback: (_, value) => singleStartRequested = value, name: "LPTIM start in single mode (SNGSTRT)")
                    .WithFlag(2, FieldMode.Write, writeCallback: (_, value) => continuousStartRequested = value, name: "Timer start in continuous mode (CNTSTRT)")
                    .WithFlag(3, FieldMode.Write, writeCallback: (_, value) =>
                    {
                        if(value)
                        {
                            ResetValue();
                            UpdateCompareTimers();
                        }
                    }, name: "Counter reset (COUNTRST)")
                    .WithTaggedFlag("Reset after read enable (RSTARE)", 4)
                    .WithReservedBits(5, 27)
                    .WithWriteCallback((_, __) =>
                    {
                        var startInSingleMode = singleStartRequested;
                        var startInContinuousMode = continuousStartRequested;
                        singleStartRequested = false;
                        continuousStartRequested = false;

                        if(enabled.Value)
                        {
                            if(startInSingleMode && startInContinuousMode)
                            {
                                this.Log(LogLevel.Warning, "Selected both single and continuous modes. Ignoring operation");
                                return;
                            }

                            if(startInSingleMode)
                            {
                                this.Log(LogLevel.Debug, "Enabling timer in single-shot mode");
                                Mode = WorkMode.OneShot;
                                Enabled = true;
                                UpdateCompareTimers();
                            }

                            if(startInContinuousMode)
                            {
                                this.Log(LogLevel.Debug, "Enabling timer in continuous mode");
                                Mode = WorkMode.Periodic;
                                Enabled = true;
                                UpdateCompareTimers();
                            }
                        }
                        else
                        {
                            this.Log(LogLevel.Debug, "Disabling timer");
                            Enabled = false;
                            DisableCompareTimers();
                        }
                    })
                },
                {(long)Registers.CaptureCompare1, new DoubleWordRegister(this)
                    .WithValueField(0, 16, out compareValues[0], name: "Capture/compare 1 value (CCR1)", writeCallback: (_, value) =>
                    {
                        compareRegisterUpdateOkStatus[0].Value = true;
                        TryEnableCompareTimer(0, value);
                        UpdateInterrupts();
                    })
                    .WithReservedBits(16, 16)
                },
                {(long)Registers.AutoReload, new DoubleWordRegister(this, 0x1)
                    .WithValueField(0, 16,
                        writeCallback: (_, value) =>
                        {
                            if(value == 0)
                            {
                                this.Log(LogLevel.Warning, "Ignoring unsupported ARR value 0");
                                return;
                            }
                            Limit = value;
                            autoReloadRegisterUpdateOkStatus.Value = true;
                            UpdateCompareTimers();
                            UpdateInterrupts();
                        },
                        valueProviderCallback: _ => (uint)Limit,
                        name: "Autoreload register (ARR)")
                    .WithReservedBits(16, 16)
                },
                {(long)Registers.Counter, new DoubleWordRegister(this)
                    .WithValueField(0, 16, FieldMode.Read, valueProviderCallback: _ => (uint)Value, name: "Counter value (CNT)")
                    .WithReservedBits(16, 16)
                },
                {(long)Registers.Configuration2, new DoubleWordRegister(this)
                    .WithTag("LPTIM input 1 selection (IN1SEL)", 0, 2)
                    .WithReservedBits(2, 2)
                    .WithTag("LPTIM input 2 selection (IN2SEL)", 4, 2)
                    .WithReservedBits(6, 10)
                    .WithTag("LPTIM input capture 1 selection (IC1SEL)", 16, 2)
                    .WithReservedBits(18, 2)
                    .WithTag("LPTIM input capture 2 selection (IC2SEL)", 20, 2)
                    .WithReservedBits(22, 10)
                },
                {(long)Registers.Repetition, new DoubleWordRegister(this)
                    .WithTag("Repetition register value (REP)", 0, 8)
                    .WithReservedBits(8, 24)
                    .WithWriteCallback((_, __) =>
                    {
                        repetitionUpdateOkStatus.Value = true;
                        UpdateInterrupts();
                    })
                },
                {(long)Registers.CaptureCompareMode1, new DoubleWordRegister(this)
                    .WithTag("Capture/compare 1 selection (CC1SEL)", 0, 1)
                    .WithTag("Capture/compare 1 output enable (CC1E)", 1, 1)
                    .WithTag("Capture/compare 1 output polarity (CC1P)", 2, 2)
                    .WithReservedBits(4, 4)
                    .WithTag("Input capture 1 prescaler (IC1PSC)", 8, 2)
                    .WithReservedBits(10, 2)
                    .WithTag("Input capture 1 filter (IC1F)", 12, 2)
                    .WithReservedBits(14, 2)
                    .WithTag("Capture/compare 2 selection (CC2SEL)", 16, 1)
                    .WithTag("Capture/compare 2 output enable (CC2E)", 17, 1)
                    .WithTag("Capture/compare 2 output polarity (CC2P)", 18, 2)
                    .WithReservedBits(20, 4)
                    .WithTag("Input capture 2 prescaler (IC2PSC)", 24, 2)
                    .WithReservedBits(26, 2)
                    .WithTag("Input capture 2 filter (IC2F)", 28, 2)
                    .WithReservedBits(30, 2)
                },
                {(long)Registers.CaptureCompare2, new DoubleWordRegister(this)
                    .WithValueField(0, 16, out compareValues[1], name: "Capture/compare 2 value (CCR2)", writeCallback: (_, value) =>
                    {
                        compareRegisterUpdateOkStatus[1].Value = true;
                        TryEnableCompareTimer(1, value);
                        UpdateInterrupts();
                    })
                    .WithReservedBits(16, 16)
                },
            };

            registers = new DoubleWordRegisterCollection(this, registersMap);
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public override void Reset()
        {
            base.Reset();
            foreach(var compareTimer in compareTimers)
            {
                compareTimer.Reset();
            }
            registers.Reset();
            IRQ.Set(false);
        }

        public GPIO IRQ { get; }

        public long Size => 0x400;

        private void ClearFlag(bool value, IFlagRegisterField flag)
        {
            if(value)
            {
                flag.Value = false;
            }
        }

        private void DisableCompareTimers()
        {
            foreach(var compareTimer in compareTimers)
            {
                compareTimer.Enabled = false;
            }
        }

        private void UpdateCompareTimers()
        {
            for(var i = 0; i < CompareChannelsCount; i++)
            {
                if(compareValues[i].Value == 0)
                {
                    continue;
                }

                TryEnableCompareTimer(i, compareValues[i].Value);
            }
        }

        private void UpdateInterrupts()
        {
            var flag = false;

            flag |= captureCompareInterruptEnable[0].Value && captureCompareInterruptStatus[0].Value;
            flag |= captureCompareInterruptEnable[1].Value && captureCompareInterruptStatus[1].Value;
            flag |= autoReloadMatchInterruptEnable.Value && autoReloadMatchInterruptStatus.Value;
            flag |= autoReloadRegisterUpdateOkEnable.Value && autoReloadRegisterUpdateOkStatus.Value;
            flag |= compareRegisterUpdateOkEnable[0].Value && compareRegisterUpdateOkStatus[0].Value;
            flag |= compareRegisterUpdateOkEnable[1].Value && compareRegisterUpdateOkStatus[1].Value;
            flag |= updateEventInterruptEnable.Value && updateEventInterruptStatus.Value;
            flag |= repetitionUpdateOkEnable.Value && repetitionUpdateOkStatus.Value;

            this.Log(LogLevel.Debug, "Setting IRQ to {0}", flag);
            IRQ.Set(flag);
        }

        private void TryEnableCompareTimer(int channel, ulong compareValue)
        {
            compareTimers[channel].Enabled = false;

            if(!Enabled)
            {
                return;
            }
            if(compareValue == 0)
            {
                this.Log(LogLevel.Debug, "Compare {0} value cannot be 0. Timer will not be set", channel + 1);
                return;
            }

            var autoReloadValue = GetValueAndLimit(out var autoReloadLimit);
            if(compareValue >= autoReloadLimit)
            {
                this.Log(LogLevel.Warning, "Compare {0} value ({1}) cannot be greater than or equal to auto reload limit ({2}). Compare value will be ignored", channel + 1, compareValue, autoReloadLimit);
                return;
            }

            if(compareValue > autoReloadValue)
            {
                compareTimers[channel].Limit = compareValue - autoReloadValue;
                compareTimers[channel].Enabled = true;
            }
        }

        private const int CompareChannelsCount = 2;

        private readonly LimitTimer[] compareTimers;
        private readonly DoubleWordRegisterCollection registers;

        private readonly IValueRegisterField[] compareValues = new IValueRegisterField[CompareChannelsCount];

        private readonly IFlagRegisterField[] captureCompareInterruptEnable = new IFlagRegisterField[CompareChannelsCount];
        private readonly IFlagRegisterField[] captureCompareInterruptStatus = new IFlagRegisterField[CompareChannelsCount];
        private readonly IFlagRegisterField[] compareRegisterUpdateOkEnable = new IFlagRegisterField[CompareChannelsCount];
        private readonly IFlagRegisterField[] compareRegisterUpdateOkStatus = new IFlagRegisterField[CompareChannelsCount];

        private readonly IFlagRegisterField autoReloadMatchInterruptEnable;
        private readonly IFlagRegisterField autoReloadMatchInterruptStatus;
        private readonly IFlagRegisterField autoReloadRegisterUpdateOkEnable;
        private readonly IFlagRegisterField autoReloadRegisterUpdateOkStatus;
        private readonly IFlagRegisterField interruptEnableRegisterUpdateOkStatus;
        private readonly IFlagRegisterField repetitionUpdateOkEnable;
        private readonly IFlagRegisterField repetitionUpdateOkStatus;
        private readonly IFlagRegisterField updateEventInterruptEnable;
        private readonly IFlagRegisterField updateEventInterruptStatus;

        private enum Registers : long
        {
            InterruptAndStatus = 0x00,
            InterruptClear = 0x04,
            InterruptEnable = 0x08,
            Configuration = 0x0C,
            Control = 0x10,
            CaptureCompare1 = 0x14,
            AutoReload = 0x18,
            Counter = 0x1C,
            Configuration2 = 0x24,
            Repetition = 0x28,
            CaptureCompareMode1 = 0x2C,
            CaptureCompare2 = 0x34,
        }
    }
}
