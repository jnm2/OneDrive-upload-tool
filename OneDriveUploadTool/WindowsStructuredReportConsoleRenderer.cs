using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Techsola;

[module: DefaultCharSet(CharSet.Unicode)]

namespace OneDriveUploadTool
{
    public sealed partial class WindowsStructuredReportConsoleRenderer
    {
        private readonly object lockObject = new object();
        private readonly IntPtr outputHandle = GetStdHandle(STD_HANDLE.OUTPUT);
        private readonly CHAR_INFO[,] buffer;
        private readonly COORD bufferSize;

        public WindowsStructuredReportConsoleRenderer()
        {
            Console.OutputEncoding = Encoding.Unicode;

            bufferSize.X = 256;
            bufferSize.Y = 256;
            buffer = new CHAR_INFO[bufferSize.Y, bufferSize.X];

            var currentAttributes = (CharInfoAttributes)(((ushort)Console.BackgroundColor << 4) | (ushort)Console.ForegroundColor);

            for (var y = 0; y < bufferSize.Y; y++)
            {
                for (var x = 0; x < bufferSize.X; x++)
                {
                    buffer[y, x].Attributes = currentAttributes;
                }
            }
        }

        public void Render(StructuredReport report)
        {
            lock (lockObject)
            {
                var writer = new BufferWriter(buffer);
                Render(ref writer, report);
                var writeRegion = new SMALL_RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = (short)(bufferSize.X - 1),
                    Bottom = (short)(bufferSize.Y - 1),
                };

                if (!WriteConsoleOutput(outputHandle, buffer, bufferSize, dwBufferCoord: default, ref writeRegion))
                    throw new Win32Exception();

                for (var y = 0; y <= writer.MaxY; y++)
                {
                    for (var x = 0; x <= writer.MaxX; x++)
                    {
                        buffer[y, x].UnicodeChar = '\0';
                    }
                }
            }
        }

        private static void Render(ref BufferWriter writer, StructuredReport report)
        {
            writer.Write(report.ToString());
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/console/getstdhandle
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(STD_HANDLE nStdHandle);

        private enum STD_HANDLE : int
        {
            INPUT = -10,
            OUTPUT = -11,
            ERROR = -12,
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/console/writeconsoleoutput
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteConsoleOutput(IntPtr hConsoleOutput, [In] CHAR_INFO[,] lpBuffer, COORD dwBufferSize, COORD dwBufferCoord, ref SMALL_RECT lpWriteRegion);

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/console/char-info-str
        /// </summary>
        private struct CHAR_INFO
        {
            public char UnicodeChar;
            public CharInfoAttributes Attributes;
        }

        [Flags]
        private enum CharInfoAttributes : ushort
        {
            FOREGROUND_BLUE = 1 << 0,
            FOREGROUND_GREEN = 1 << 1,
            FOREGROUND_RED = 1 << 2,
            FOREGROUND_INTENSITY = 1 << 3,
            BACKGROUND_BLUE = 1 << 4,
            BACKGROUND_GREEN = 1 << 5,
            BACKGROUND_RED = 1 << 6,
            BACKGROUND_INTENSITY = 1 << 7,
            COMMON_LVB_LEADING_BYTE = 1 << 8,
            COMMON_LVB_TRAILING_BYTE = 1 << 9,
            COMMON_LVB_GRID_HORIZONTAL = 1 << 10,
            COMMON_LVB_GRID_LVERTICAL = 1 << 11,
            COMMON_LVB_GRID_RVERTICAL = 1 << 12,
            COMMON_LVB_REVERSE_VIDEO = 1 << 14,
            COMMON_LVB_UNDERSCORE = 1 << 15,
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/console/coord-str
        /// </summary>
        private struct COORD
        {
            public short X;
            public short Y;
        }

        /// <summary>
        /// https://docs.microsoft.com/en-us/windows/console/small-rect-str
        /// </summary>
        private struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }
    }
}
