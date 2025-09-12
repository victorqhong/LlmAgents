namespace RLArena;

public class CircularBuffer<T>
{
    private readonly T[] buffer;
    private readonly int capacity;
    private int head;
    private int count;

    public CircularBuffer(int n)
    {
        if (n <= 0) throw new ArgumentException("Capacity must be positive.");
        capacity = n;
        buffer = new T[n];
        head = 0;
        count = 0;
    }

    public void Add(T item)
    {
        buffer[head] = item;
        head = (head + 1) % capacity;
        if (count < capacity) count++;
    }

    public IReadOnlyList<T> GetItems()
    {
        var result = new T[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = buffer[(head - count + i + capacity) % capacity];
        }
        return result.AsReadOnly();
    }
}