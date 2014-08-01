﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ECore.DeviceMemories
{
    public enum REG
    {
		STROBE_UPDATE = 0,
		SPI_ADDRESS = 1,
		SPI_WRITE_VALUE = 2,
		TRIGGER_LEVEL = 3,
		TRIGGER_MODE = 22,
		TRIGGER_WIDTH = 23,
		TRIGGERHOLDOFF_B0 = 4,
		TRIGGERHOLDOFF_B1 = 5,
		TRIGGERHOLDOFF_B2 = 20,
		TRIGGERHOLDOFF_B3 = 21,
		SAMPLECLOCKDIVIDER_B0 = 6,
		SAMPLECLOCKDIVIDER_B1 = 7,
		CHA_YOFFSET_VOLTAGE = 8,
		CHB_YOFFSET_VOLTAGE = 9,
		DIVIDER_MULTIPLIER = 10,
		DIGITAL_OUT = 11,
		TRIGGER_PWM = 12,
		ACQUISITION_MULTIPLE_POWER = 18,
		TRIGGER_THRESHOLD = 19,
		VIEW_DECIMATION = 13,
		VIEW_OFFSET = 14,
		VIEW_ACQUISITIONS = 15,
		VIEW_BURSTS = 16,
		AWG_DEBUG = 17,
    }

    public enum STR
    {
		GLOBAL_RESET = 0,
		INIT_SPI_TRANSFER = 1,
		AWG_ENABLE = 2,
		LA_ENABLE = 3,
		SCOPE_ENABLE = 4,
		SCOPE_UPDATE = 5,
		FORCE_TRIGGER = 6,
		ACQ_START = 9,
		ACQ_STOP = 10,
		CHA_DCCOUPLING = 13,
		CHB_DCCOUPLING = 14,
		ENABLE_ADC = 15,
		OVERFLOW_DETECT = 16,
		ENABLE_NEG = 17,
		ENABLE_RAM = 19,
		DEBUG_PIC = 20,
		DEBUG_RAM = 21,
		DOUT_3V_5V = 22,
		EN_OPAMP_B = 23,
		AWG_DEBUG = 24,
		DIGI_DEBUG = 25,
    }

    public enum ROM
    {
		FW_MSB = 0,
		FW_LSB = 1,
		FW_GIT0 = 2,
		FW_GIT1 = 3,
		FW_GIT2 = 4,
		FW_GIT3 = 5,
		SPI_RECEIVED_VALUE = 6,
		STROBES = 7,
    }

}
