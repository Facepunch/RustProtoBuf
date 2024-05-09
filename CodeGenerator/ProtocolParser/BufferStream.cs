public sealed partial class BufferStream : IDisposable
{
    // Putting this in a nested class to avoid IL2CPP overhead for classes with static constructors
    public static class Shared
    {
        public static int StartingCapacity = 64;
        public static int MaximumCapacity = 128 * 1024 * 1024;
        public static readonly Facepunch.ArrayPool<byte> ArrayPool = new(128 * 1024 * 1024);
    }
    
    private bool _isBufferOwned;
    private byte[] _buffer;
    private int _length;
    private int _position;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (_position > value)
            {
                throw new InvalidOperationException($"Cannot shrink buffer below current position!");
            }

            var growSize = value - _length;
            if (growSize > 0)
            {
                EnsureCapacity(growSize);
            }

            _length = value;
        }
    }
    
    public int Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position; 
    }

    public BufferStream Initialize()
    {
        _isBufferOwned = true;
        _buffer = null;
        _length = 0;
        _position = 0;
        return this;
    }

    public BufferStream Initialize(Span<byte> buffer)
    {
        _isBufferOwned = true; // we need to copy the data into our own buffer
        _buffer = null;
        _length = buffer.Length;
        _position = 0;

        EnsureCapacity(buffer.Length);
        buffer.CopyTo(_buffer);
        
        return this;
    }
    
    public BufferStream Initialize(byte[] buffer, int length = -1)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }
        
        if (length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        
        _isBufferOwned = false;
        _buffer = buffer;
        _length = length < 0 ? buffer.Length : length;
        _position = 0;
        return this;
    }

    public void Dispose()
    {
        if (_isBufferOwned && _buffer != null)
        {
            Shared.ArrayPool.Return(_buffer);
        }
        
        _buffer = null;

        var instance = this;
        Facepunch.Pool.Free(ref instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadByte()
    {
        if (_position >= _length)
        {
            return -1;
        }

        return _buffer[_position++];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte b)
    {
        EnsureCapacity(1);
        _buffer[_position++] = b;
        _length = Math.Max(_length, _position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Read<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        if (_length - _position < size)
        {
            ThrowReadOutOfBounds();
        }

        ref readonly var value = ref Unsafe.As<byte, T>(ref _buffer[_position]);
        _position += size;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Peek<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        if (_length - _position < size)
        {
            ThrowReadOutOfBounds();
        }

        ref readonly var value = ref Unsafe.As<byte, T>(ref _buffer[_position]);
        return value;
    }
    
    // Separate method to help with inlining of callers (throw expressions don't inline well)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowReadOutOfBounds()
    {
        throw new InvalidOperationException("Attempted to read past the end of the BufferStream");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write<T>(T value) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        EnsureCapacity(size);
        Unsafe.As<byte, T>(ref _buffer[_position]) = value;
        _position += size;
        _length = Math.Max(_length, _position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RangeHandle GetRange(int count)
    {
        EnsureCapacity(count);
        var handle = new RangeHandle(this, _position, count);
        _position += count;
        _length = Math.Max(_length, _position);
        return handle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
         // todo: bounds checks?
        _position += count;
    }

    public ArraySegment<byte> GetBuffer()
    {
        return new ArraySegment<byte>(_buffer, 0, _length);
    }
    
    private void EnsureCapacity(int spaceRequired)
    {
        if (spaceRequired < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spaceRequired));
        }

        if (_buffer == null)
        {
            if (!_isBufferOwned)
            {
                throw new InvalidOperationException("Cannot allocate for BufferStream that doesn't own the buffer (did you forget to call Initialize?)");
            }
            
            var initialRequiredCapacity = spaceRequired <= Shared.StartingCapacity
                ? Shared.StartingCapacity
                : spaceRequired;
            var capacity = Mathf.NextPowerOfTwo(initialRequiredCapacity);

            if (capacity > Shared.MaximumCapacity)
            {
                throw new Exception($"Preventing BufferStream buffer from growing too large (requiredLength={initialRequiredCapacity})");
            }

            _buffer = Shared.ArrayPool.Rent(capacity);
            return;
        }

        if (_buffer.Length - _position >= spaceRequired)
        {
            return;
        }

        var requiredLength = _position + spaceRequired;
        var newCapacity = Mathf.NextPowerOfTwo(Math.Max(requiredLength, _buffer.Length));
        
        if (!_isBufferOwned)
        {
            throw new InvalidOperationException($"Cannot grow buffer for BufferStream that doesn't own the buffer (requiredLength={requiredLength})");
        }
        
        if (newCapacity > Shared.MaximumCapacity)
        {
            throw new Exception($"Preventing BufferStream buffer from growing too large (requiredLength={requiredLength})");
        }

        var newBuffer = Shared.ArrayPool.Rent(newCapacity);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
        Shared.ArrayPool.Return(_buffer);
        _buffer = newBuffer;
    }
    
    public readonly ref struct RangeHandle
    {
        private readonly BufferStream _stream;
        private readonly int _offset;
        private readonly int _length;

        public RangeHandle(BufferStream stream, int offset, int length)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _offset = offset;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan()
        {
            return new Span<byte>(_stream._buffer, _offset, _length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArraySegment<byte> GetSegment()
        {
            return new ArraySegment<byte>(_stream._buffer, _offset, _length);
        }
    }
}
