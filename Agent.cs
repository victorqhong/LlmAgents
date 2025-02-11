using System.Diagnostics;

public class Agent
{
    public string MessagesFile { get; private set; }
    public bool SaveMessages { get; set; }
    public string ModelName { get; set; } = "gpt-4o";
    public string CredentialsFile { get; set; }

    public Agent(bool saveMessages, string messagesFile, string credentialsFile, string modelName)
    {
        SaveMessages = saveMessages;
        MessagesFile = messagesFile;
        CredentialsFile = credentialsFile;
        ModelName = modelName;
    }

    public string Input(string input)
    {
        return GenerateCompletion(input);
    }

    private string GenerateCompletion(string userMessage)
    {
        var process = new Process();
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.FileName = "python3";
        process.StartInfo.ArgumentList.Add("/home/victor/Code/llm/llm.py");
        process.StartInfo.ArgumentList.Add("--user_message");
        process.StartInfo.ArgumentList.Add(userMessage);
        process.StartInfo.ArgumentList.Add("--messages");
        process.StartInfo.ArgumentList.Add(MessagesFile);
        process.StartInfo.ArgumentList.Add("--save_messages");
        process.StartInfo.ArgumentList.Add(SaveMessages.ToString());
        process.StartInfo.ArgumentList.Add("--model_name");
        process.StartInfo.ArgumentList.Add(ModelName);
        process.StartInfo.ArgumentList.Add("--credentials_file");
        process.StartInfo.ArgumentList.Add(CredentialsFile);
        process.Start();
        process.WaitForExit();

        return process.StandardOutput.ReadToEnd();
    }
}
