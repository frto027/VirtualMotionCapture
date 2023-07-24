using Google.Protobuf;
using Messages;
using System;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;




    abstract class SlimeVRBridge
    {
        public enum SlimeVRPosition
        {
            None = 0,
            Waist,
            LeftFoot,
            RightFoot,
            Chest,
            LeftKnee,
            RightKnee,
            LeftElbow,
            RightElbow,
            LeftShoulder,
            RightShoulder,
            LeftHand,
            RightHand,
            LeftController,
            RightController,
            Head,
            Neck,
            Camera,
            Keyboard,
            HMD,
            Beacon,
            GenericController
        }

        public static string[] PositionNames = new string[]{
            "None",
            "Waist",
            "LeftFoot",
            "RightFoot",
            "Chest",
            "LeftKnee",
            "RightKnee",
            "LeftElbow",
            "RightElbow",
            "LeftShoulder",
            "RightShoulder",
            "LeftHand",
            "RightHand",
            "LeftController",
            "RightController",
            "Head",
            "Neck",
            "Camera",
            "Keyboard",
            "HMD",
            "Beacon",
            "GenericController"
        };

        public abstract ProtobufMessage getNextMessage();
        public abstract bool sendMessage(ProtobufMessage msg);//the message WILL delay send for speed up.
        //WARNING: the data is buffered, you MUST call flush after add message via sendMessage.
        public abstract bool flush();

        private static SlimeVRBridge VMCInstance;

        public static SlimeVRBridge getVMCInstance()
        {
            if (VMCInstance == null)
                VMCInstance = new NamedPipeBridge("\\\\.\\pipe\\VMCVRInput");
            return VMCInstance;
        }

        private static SlimeVRBridge DriverInstance;

        public static SlimeVRBridge getDriverInstance()
        {
            if (DriverInstance == null)
                DriverInstance = new NamedPipeBridge("\\\\.\\pipe\\SlimeVRDriver");
            return DriverInstance;
        }

        public abstract void close();

        public abstract void connect();
        public abstract void reset();
    }

sealed class NamedPipeBridge : SlimeVRBridge
{
    [DllImport("kernel32.dll")]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
    [DllImport("kernel32.dll")]
    private static extern bool PeekNamedPipe(IntPtr hNamedPipe, [Out] byte[] lpBuffer, uint nBufferSize, out uint lpBytesRead, IntPtr lpTotalBytesAvail, IntPtr lpBytesLeftThisMessage);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr CreateFileA(
         [MarshalAs(UnmanagedType.LPStr)] string filename,
         [MarshalAs(UnmanagedType.U4)] FileAccess access,
         [MarshalAs(UnmanagedType.U4)] FileShare share,
         IntPtr securityAttributes,
         [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
         [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
         IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
    [SuppressUnmanagedCodeSecurity]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);


    byte[] sendBuffer = new byte[4096];
    int sendBufferDataCount = 0;

    private string pipe_name;

    IntPtr pipe = IntPtr.Zero;
    private static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public NamedPipeBridge(string pipe_name)
    {
        this.pipe_name = pipe_name;
    }

    byte[] size_byte = new byte[4];
    byte[] received_message_buffer = new byte[256];
    public override ProtobufMessage getNextMessage()
    {
        uint read_bytes;

        if (pipe == IntPtr.Zero)
            return null;

        if (!PeekNamedPipe(pipe, size_byte, 4, out read_bytes, IntPtr.Zero, IntPtr.Zero))
            return null;
        if (read_bytes != 4)
            return null;

        int size = (size_byte[0]) | (size_byte[1] << 8) | (size_byte[2] << 16) | (size_byte[3] << 24);

        if (received_message_buffer.Length < size)
        {
            received_message_buffer = new byte[size];
        }

        if (size < 4 || size > 4096)
        {
            UnityEngine.Debug.LogError($"Invalid data length from message({size}).");
            return null;
        }

        if (!ReadFile(pipe, received_message_buffer, (uint)size, out read_bytes, IntPtr.Zero))
        {
            return null;
        }
        if (read_bytes != size)
        {
            UnityEngine.Debug.LogWarning($"can't read enough data from pipe, {read_bytes} read, {size} expected.");
            return null;
        }
        ProtobufMessage message = ProtobufMessage.Parser.ParseFrom(received_message_buffer, 4, size - 4);
        return message;
    }

    public override bool sendMessage(ProtobufMessage msg)
    {
        if (pipe == IntPtr.Zero) return false;
        var size = msg.CalculateSize() + 4;

        if (size + sendBufferDataCount >= sendBuffer.Length)
        {
            if (!flush())
                return false;
        }

        sendBuffer[sendBufferDataCount] = (byte)(size & 0xFF);
        sendBuffer[sendBufferDataCount + 1] = (byte)((size >> 8) & 0xFF);
        sendBuffer[sendBufferDataCount + 2] = (byte)((size >> 16) & 0xFF);
        sendBuffer[sendBufferDataCount + 3] = (byte)((size >> 24) & 0xFF);

        msg.WriteTo(new MemoryStream(sendBuffer, sendBufferDataCount + 4, size - 4));

        sendBufferDataCount += size;
        return true;
    }

    public override void connect()
    {

        if (pipe != IntPtr.Zero)
        {
            reset();
        }

        pipe = CreateFileA(pipe_name, FileAccess.ReadWrite, FileShare.None, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            pipe = IntPtr.Zero;
            UnityEngine.Debug.LogWarning("slime-vr pipe create failed.");
        }
    }

    public override void reset()
    {
        if (pipe != IntPtr.Zero)
        {
            CloseHandle(pipe);
            pipe = IntPtr.Zero;
        }
    }

    public override void close()
    {
        reset();
    }

    public override bool flush()
    {
        try
        {
            uint _written = 0;

            WriteFile(pipe, sendBuffer, (uint)sendBufferDataCount, out _written, IntPtr.Zero);
            if (_written != sendBufferDataCount)
            {
                UnityEngine.Debug.LogError($"pipe sent {_written}(expected {sendBufferDataCount})");
                return false;
            }
            sendBufferDataCount = 0;
            return true;
        }
        catch (Exception e)
        {
            //unable to send, we will clear all data.
            UnityEngine.Debug.LogError(e.ToString());
            sendBufferDataCount = 0;
            return false;
        }
    }
}