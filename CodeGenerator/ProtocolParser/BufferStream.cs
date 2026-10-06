public sealed partial class BufferStream : IDisposable, Facepunch.Pool.IPooled
{
	// Putting this in a nested class to avoid IL2CPP overhead for classes with static constructors
	public static class Shared
	{
		public static int StartingCapacity = 64;
		public static int MaximumCapacity = 512 * 1024 * 1024;
		public static int MaximumPooledSize = 64 * 1024 * 1024;
		public static readonly Facepunch.ArrayPool<byte> ArrayPool = new(MaximumPooledSize);
	}

	public const int DefaultFieldOperationLimit = 4 * 1024;
	
	private bool _isBufferOwned;
	private byte[] _buffer;
	private int _length;
	private int _position;
	private int _fieldOperationLimit = -1;
	private int _remainingFieldOperations = -1;
	private string _fieldOperationContext;
	private bool _validateFieldOrder;
	private bool _rejectNonFiniteFloatingPointValues;
	private int _remainingRepeatedElements = -1;

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
		set
		{
			if (value < 0 || value > _length)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_position = value;
		}
	}

	public BufferStream Initialize()
	{
		_isBufferOwned = true;
		_buffer = null;
		_length = 0;
		_position = 0;
		_fieldOperationLimit = -1;
		_remainingFieldOperations = -1;
		_fieldOperationContext = null;
		_validateFieldOrder = false;
		_rejectNonFiniteFloatingPointValues = false;
		_remainingRepeatedElements = -1;
		return this;
	}

	public BufferStream Initialize(Span<byte> buffer)
	{
		_isBufferOwned = true; // we need to copy the data into our own buffer
		_buffer = null;
		_length = buffer.Length;
		_position = 0;
		_fieldOperationLimit = -1;
		_remainingFieldOperations = -1;
		_fieldOperationContext = null;
		_validateFieldOrder = false;
		_rejectNonFiniteFloatingPointValues = false;
		_remainingRepeatedElements = -1;

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
		_fieldOperationLimit = -1;
		_remainingFieldOperations = -1;
		_fieldOperationContext = null;
		_validateFieldOrder = false;
		_rejectNonFiniteFloatingPointValues = false;
		_remainingRepeatedElements = -1;
		return this;
	}

	public FieldOperationLimitScope BeginFieldOperationLimit(int maxOperations, string context)
	{
		if (maxOperations < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxOperations));
		}

		var appliesLimit = _remainingFieldOperations < 0;
		if (appliesLimit)
		{
			_fieldOperationLimit = maxOperations;
			_remainingFieldOperations = maxOperations;
			_validateFieldOrder = true;
		}

		var previousContext = _fieldOperationContext;
		var previousRejectNonFiniteFloatingPointValues = _rejectNonFiniteFloatingPointValues;
		_fieldOperationContext = context;
		_rejectNonFiniteFloatingPointValues = true;
		return new FieldOperationLimitScope(this, appliesLimit, previousContext, previousRejectNonFiniteFloatingPointValues);
	}

	public FieldOrderValidationScope SuspendFieldOrderValidation()
	{
		return SuspendFieldOrderValidation(true);
	}

	public FieldOrderValidationScope SuspendFieldOrderValidation(bool suspend)
	{
		var previousValue = _validateFieldOrder;
		if (suspend)
		{
			_validateFieldOrder = false;
		}
		return new FieldOrderValidationScope(this, previousValue, suspend);
	}

	public FieldOperationLimitSuspensionScope SuspendFieldOperationLimit()
	{
		return SuspendFieldOperationLimit(true);
	}

	public FieldOperationLimitSuspensionScope SuspendFieldOperationLimit(bool suspend)
	{
		var previousRemainingOperations = _remainingFieldOperations;
		if (suspend)
		{
			_remainingFieldOperations = -1;
		}
		return new FieldOperationLimitSuspensionScope(this, previousRemainingOperations, suspend);
	}

	public RepeatedElementLimitScope BeginRepeatedElementLimit(int maxElements)
	{
		var previousLimit = _remainingRepeatedElements;
		_remainingRepeatedElements = maxElements;
		return new RepeatedElementLimitScope(this, previousLimit);
	}

	public void Dispose()
	{
		if (_isBufferOwned && _buffer != null)
		{
			ReturnBuffer(_buffer);
		}
		
		_buffer = null;

		var instance = this;
		Facepunch.Pool.Free(ref instance);
	}

	void Facepunch.Pool.IPooled.EnterPool()
	{
		if (_isBufferOwned && _buffer != null)
		{
			ReturnBuffer(_buffer);
		}
		
		_buffer = null;
		_fieldOperationLimit = -1;
		_remainingFieldOperations = -1;
		_fieldOperationContext = null;
		_validateFieldOrder = false;
		_rejectNonFiniteFloatingPointValues = false;
		_remainingRepeatedElements = -1;
	}
	
	void Facepunch.Pool.IPooled.LeavePool()
	{
	}

	public void Clear()
	{
		_length = 0;
		_position = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ConsumeRepeatedElement()
	{
		if (_remainingRepeatedElements < 0)
		{
			return;
		}

		if (_remainingRepeatedElements == 0)
		{
			throw new SilentOrbit.ProtocolBuffers.ProtocolBufferException("Repeated element budget exceeded");
		}

		_remainingRepeatedElements--;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ConsumeFieldOperation(string context)
	{
		if (_remainingFieldOperations < 0)
		{
			return;
		}

		_fieldOperationContext = context;
		if (_remainingFieldOperations == 0)
		{
			throw new SilentOrbit.ProtocolBuffers.ProtocolBufferException($"Field operation budget of {_fieldOperationLimit} exceeded while reading {_fieldOperationContext}");
		}

		_remainingFieldOperations--;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ValidateFieldOrder(ref uint lastFieldId, uint fieldId, bool fieldIsRepeated, string context)
	{
		if (!_validateFieldOrder)
		{
			return;
		}

		if (fieldId < lastFieldId || (fieldId == lastFieldId && !fieldIsRepeated))
		{
			throw new SilentOrbit.ProtocolBuffers.ProtocolBufferException($"Invalid field order in {context}: previous field {lastFieldId}, received field {fieldId}");
		}

		lastFieldId = fieldId;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void ValidateFiniteValue(float value)
	{
		if (_rejectNonFiniteFloatingPointValues && (float.IsNaN(value) || float.IsInfinity(value)))
		{
			throw new SilentOrbit.ProtocolBuffers.ProtocolBufferException("Invalid float value");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void ValidateFiniteValue(double value)
	{
		if (_rejectNonFiniteFloatingPointValues && (double.IsNaN(value) || double.IsInfinity(value)))
		{
			throw new SilentOrbit.ProtocolBuffers.ProtocolBufferException("Invalid double value");
		}
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

	public readonly struct RepeatedElementLimitScope : IDisposable
	{
		private readonly BufferStream _stream;
		private readonly int _previousLimit;

		internal RepeatedElementLimitScope(BufferStream stream, int previousLimit)
		{
			_stream = stream;
			_previousLimit = previousLimit;
		}

		public void Dispose()
		{
			_stream._remainingRepeatedElements = _previousLimit;
		}
	}

	public readonly struct FieldOperationLimitScope : IDisposable
	{
		private readonly BufferStream _stream;
		private readonly bool _appliesLimit;
		private readonly string _previousContext;
		private readonly bool _previousRejectNonFiniteFloatingPointValues;

		internal FieldOperationLimitScope(BufferStream stream, bool appliesLimit, string previousContext, bool previousRejectNonFiniteFloatingPointValues)
		{
			_stream = stream;
			_appliesLimit = appliesLimit;
			_previousContext = previousContext;
			_previousRejectNonFiniteFloatingPointValues = previousRejectNonFiniteFloatingPointValues;
		}

		public void Dispose()
		{
			_stream._fieldOperationContext = _previousContext;
			_stream._rejectNonFiniteFloatingPointValues = _previousRejectNonFiniteFloatingPointValues;
			if (_appliesLimit)
			{
				_stream._fieldOperationLimit = -1;
				_stream._remainingFieldOperations = -1;
				_stream._validateFieldOrder = false;
			}
		}
	}

	public readonly struct FieldOrderValidationScope : IDisposable
	{
		private readonly BufferStream _stream;
		private readonly bool _previousValue;
		private readonly bool _suspended;

		internal FieldOrderValidationScope(BufferStream stream, bool previousValue, bool suspended)
		{
			_stream = stream;
			_previousValue = previousValue;
			_suspended = suspended;
		}

		public void Dispose()
		{
			if (_suspended)
			{
				_stream._validateFieldOrder = _previousValue;
			}
		}
	}

	public readonly struct FieldOperationLimitSuspensionScope : IDisposable
	{
		private readonly BufferStream _stream;
		private readonly int _previousRemainingOperations;
		private readonly bool _suspended;

		internal FieldOperationLimitSuspensionScope(BufferStream stream, int previousRemainingOperations, bool suspended)
		{
			_stream = stream;
			_previousRemainingOperations = previousRemainingOperations;
			_suspended = suspended;
		}

		public void Dispose()
		{
			if (_suspended)
			{
				_stream._remainingFieldOperations = _previousRemainingOperations;
			}
		}
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
		RequireRemaining(count);
		_position += count;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RequireRemaining(int count)
	{
		if (count < 0 || count > _length - _position)
		{
			ThrowReadOutOfBounds();
		}
	}

	public ArraySegment<byte> GetBuffer()
	{
		if (_length == 0)
		{
			return new ArraySegment<byte>(Array.Empty<byte>(), 0, 0);
		}

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

			_buffer = RentBuffer(capacity);
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

		var newBuffer = RentBuffer(newCapacity);
		Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
		ReturnBuffer(_buffer);
		_buffer = newBuffer;
	}

	private static byte[] RentBuffer(int minSize)
	{
		if (minSize > Shared.MaximumPooledSize)
		{
			return new byte[minSize];
		}
		
		return Shared.ArrayPool.Rent(minSize);
	}

	private static void ReturnBuffer(byte[] buffer)
	{
		if (buffer == null ||
			buffer.Length > Shared.MaximumPooledSize)
		{
			return;
		}
		
		Shared.ArrayPool.Return(buffer);
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
