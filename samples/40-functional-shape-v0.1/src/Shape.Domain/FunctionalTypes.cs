namespace Shape.Domain;

public abstract class Result<TValue, TError>
    where TValue : notnull
    where TError : notnull
{
    private Result()
    {
    }

    public sealed class Success : Result<TValue, TError>
    {
        public Success(TValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public TValue Value { get; }
    }

    public sealed class Failure : Result<TValue, TError>
    {
        public Failure(TError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            Error = error;
        }

        public TError Error { get; }
    }
}

public abstract class Option<T>
    where T : notnull
{
    private Option()
    {
    }

    public sealed class Some : Option<T>
    {
        public Some(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    public sealed class None : Option<T>;
}
