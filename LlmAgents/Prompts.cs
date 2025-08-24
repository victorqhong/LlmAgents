
namespace LlmAgents
{
    public static class Prompts
    {
        public static string DefaultSystemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication. You are expected to implement code changes. If something needs clarification, how to proceed, or given a choice, use the 'question_ask' tool.";

        public static string ToolsPrompt = "Summarize the tools available and their parameters";

        public static string QuestionnairePrompt = "Write a questionnaire to gather requirements for a new software project minimum viable product. Save the file to MVP.md";

        public static string PlanPrompt = "Read the file 'MVP.md' and generate an implementation plan, and save the file to PLAN.md";

        public static string TodoPrompt = "Read the file 'PLAN.md' and create todos in appropriate groups. Each phase should have one or more todos.";
    }
}
