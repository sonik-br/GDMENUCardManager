using System;
using System.Runtime.InteropServices;

namespace GDMENUCardManager
{
    internal class PowerManager
    {
        [Flags]
        private enum EXECUTION_STATE : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE flags);

        internal static void PreventSleep() => SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);

        internal static void AllowSleep() => SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
    }
}
