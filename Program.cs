var agent1 = new Agent(true, $"{System.Environment.CurrentDirectory}/messages1.json", "/home/victor/.aoai_credentials.json", "gpt-4o");
var prompt1 = "Tell me a fact about the Roman empire";
var response1 = agent1.Input(prompt1);
Console.WriteLine($"Prompt: {prompt1}");
Console.WriteLine($"Response1: {response1}");

var agent2 = new Agent(true, $"{System.Environment.CurrentDirectory}/messages2.json", "/home/victor/.aoai_credentials.json", "gpt-4o");
var prompt2 = $"What do you think of this fact about the Roman empire? Fact: {response1}";
var response2 = agent2.Input(prompt2);
Console.WriteLine($"Prompt: {prompt2}");
Console.WriteLine($"Response2: {response2}");

Console.ReadLine();

