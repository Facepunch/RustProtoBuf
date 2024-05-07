public ref struct BufferStream
{
    private Span<byte> _buffer;
    private int _position;

    public int Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position; 
    }

    public BufferStream(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }
    
    public BufferStream(byte[] buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadByte()
    {
        if (_position >= _buffer.Length)
        {
            return -1;
        }

        return _buffer[_position++];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Byte()
    {
        if (_position >= _buffer.Length)
        {
            throw new InvalidOperationException("Attempted to access more bytes than the buffer contains.");
        }

        return ref Unsafe.Add(ref _buffer.GetPinnableReference(), _position++);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Data<T>() where T : unmanaged
    {
        var bytes = Bytes(Unsafe.SizeOf<T>());
        return ref Unsafe.As<byte, T>(ref bytes.GetPinnableReference());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> Bytes(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (_position + count >= _buffer.Length)
        {
            throw new InvalidOperationException("Attempted to access more bytes than the buffer contains.");
        }

        var bytes = _buffer.Slice(_position, count);
        _position += count;
        return bytes;
    }
}
