using LlmAgents.Agents;
using Newtonsoft.Json.Linq;

namespace RLArena;

internal class Environment
{
    private readonly LlmAgent agent;

    private readonly string workingDirectory;

    public Environment(LlmAgent agent, string workingDirectory)
    {
        this.agent = agent;
        this.workingDirectory = workingDirectory;

        WorkingDirectory = workingDirectory;
    }

    public string WorkingDirectory { get; private set; }

    public bool ResettingEnvironment { get; private set; }

    public void ResetEnvironment()
    {
        ResettingEnvironment = true;

        WorkingDirectory = workingDirectory;
        agent.FindTool("directory_change")?.Invoke(JObject.FromObject(new { path = WorkingDirectory }));
        foreach (var directory in Directory.EnumerateDirectories(WorkingDirectory, "*.*", SearchOption.AllDirectories))
        {
            Directory.Delete(directory, true);
        }

        foreach (var file in Directory.EnumerateFiles(WorkingDirectory, "*.*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        ResettingEnvironment = false;
    }
}