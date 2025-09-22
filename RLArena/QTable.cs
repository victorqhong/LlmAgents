namespace RLArena;

internal class QTable
{
    public const int NUM_ACTIONS = 11;

    public Dictionary<string, double[]> Table = new Dictionary<string, double[]>();

    private string StateToKey(States state, params Actions[] actions)
    {
        return $"{state}|{string.Join('|', actions)}";
    }

    public (double[], string) GetQTableEntry(States state, CircularBuffer<Actions> buffer)
    {
        var items = buffer.GetItems();

        string key;
        switch (items.Count)
        {
            case 0:
                key = StateToKey(state);
                break;
            case 1:
                key = StateToKey(state, items[0]);
                break;
            case 2:
                key = StateToKey(state, items[0], items[1]);
                break;
            case 3:
                key = StateToKey(state, items[0], items[1], items[2]);
                break;
            case 4:
                key = StateToKey(state, items[0], items[1], items[2], items[3]);
                break;
            case 5:
                key = StateToKey(state, items[0], items[1], items[2], items[3], items[4]);
                break;
            default:
                throw new Exception();
        }

        if (!Table.TryGetValue(key, out var qVals))
        {
            qVals = new double[NUM_ACTIONS];
            Table[key] = qVals;
        }

        return (qVals, key);
    }

    public double[] GetQValues(States state, CircularBuffer<Actions> buffer)
    {
        return GetQTableEntry(state, buffer).Item1;
    }

    public double MaxQ(States state, CircularBuffer<Actions> buffer)
    {
        var qVals = GetQValues(state, buffer);
        return qVals.Max();
    }

    public int ArgMaxQ(States state, CircularBuffer<Actions> buffer)
    {
        var qVals = GetQValues(state, buffer);
        double maxQ = qVals[0];
        int maxIdx = 0;
        for (int i = 1; i < qVals.Length; i++)
        {
            if (qVals[i] > maxQ)
            {
                maxQ = qVals[i];
                maxIdx = i;
            }
        }
        return maxIdx;
    }

    public void LoadQTable(string path)
    {
        Table = [];
        if (!File.Exists(path))
        {
            return;
        }

        using var br = new BinaryReader(File.OpenRead(path));
        var count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();

            var value = new double[NUM_ACTIONS];
            for (int j = 0; j < value.Length; j++)
            {
                value[j] = br.ReadDouble();
            }

            if (Table.ContainsKey(key))
            {
                throw new Exception();
            }
            else
            {
                Table.Add(key, value);
            }
        }
    }

    public void SaveQTable(string path)
    {
        using var bw = new BinaryWriter(File.OpenWrite(path));
        bw.Write(Table.Count);
        foreach (var kvp in Table)
        {
            bw.Write(kvp.Key);
            for (int j = 0; j < NUM_ACTIONS; j++)
            {
                bw.Write(kvp.Value[j]);
            }
        }
    }
}