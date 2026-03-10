namespace AgentManager.Models.Containers;

using System.Text.Json.Serialization;

public class InstancesGetResponse : LxdResponse
{
    private List<string>? instances = null;

    [JsonIgnore]
    public List<string> Instances
    {
        get
        {
            if (instances != null)
            {
                return instances;
            }

            instances = [];
            foreach (var value in Metadata.EnumerateArray())
            {
                var instance = value.GetString();
                if (string.IsNullOrEmpty(instance))
                {
                    continue;
                }

                instances.Add(instance[15..]);
            }

            return instances;
        }
    }
}
