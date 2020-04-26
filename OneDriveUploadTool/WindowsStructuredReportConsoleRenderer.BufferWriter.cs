namespace OneDriveUploadTool
{
    partial class WindowsStructuredReportConsoleRenderer
    {
        private struct BufferWriter
        {
            private readonly CHAR_INFO[,] buffer;

            private int x;
            private int y;

            public int MaxX { get; private set; }
            public int MaxY { get; private set; }

            public BufferWriter(CHAR_INFO[,] buffer)
            {
                this.buffer = buffer;
                x = 0;
                y = 0;
                MaxX = 0;
                MaxY = 0;
            }

            public void Write(char value)
            {
                if (x >= buffer.GetLength(1))
                {
                    x = 0;
                    y++;
                }

                if (y >= buffer.GetLength(0)) return;

                buffer[y, x].UnicodeChar = value;
                x++;
                if (MaxX < x) MaxX = x;
                if (MaxY < y) MaxY = y;
            }

            public void Write(string value)
            {
                foreach (var character in value)
                {
                    switch (character)
                    {
                        case '\r':
                            x = 0;
                            break;
                        case '\n':
                            x = 0;
                            y++;
                            break;
                        default:
                            Write(character);
                            break;
                    }
                }
            }
        }
    }
}
