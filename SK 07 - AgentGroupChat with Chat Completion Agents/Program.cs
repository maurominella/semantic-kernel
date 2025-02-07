// Copyright (c) Microsoft. All rights reserved.

using LLMSettings;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;


internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var settings = new Settings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {settings.AzureOpenAI.ChatModelDeployment}");

        // Create the Azure Chat Completion object, e.g. the pointer to Azure OpenAI
        var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: settings.AzureOpenAI.ChatModelDeployment,
            endpoint: settings.AzureOpenAI.Endpoint,
            apiKey: settings.AzureOpenAI.ApiKey
        );

        // Build the kernel, that already integrates the AzureOpenAIChatCompletion object
        Kernel kernel = builder.Build();

        // Add enterprise logging components
        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

        // Create agent code_validator
        string codevalidator_agent_name = "CodeValidator";
        string codevalidator_agent_instruction = ReadAgentInstructions(codevalidator_agent_name);
        var codevalidator_agent = CreateAgent(name: codevalidator_agent_name, instructions: codevalidator_agent_instruction, kernel: kernel);

        // Create agent junior_developer
        string juniordeveloper_agent_name = "JuniorDeveloper";
        string juniordeveloper_agent_instruction = ReadAgentInstructions(juniordeveloper_agent_name);
        var juniordeveloper_agent = CreateAgent(name: juniordeveloper_agent_name, instructions: juniordeveloper_agent_instruction, kernel: kernel);

        // Create agent senior_developer
        string seniordeveloper_agent_name = "SeniorDeveloper";
        string seniordeveloper_agent_instruction = ReadAgentInstructions(seniordeveloper_agent_name);
        var seniordeveloper_agent = CreateAgent(name: seniordeveloper_agent_name, instructions: seniordeveloper_agent_instruction, kernel: kernel);

        // termination function
        var terminate_function = KernelFunctionFactory.CreateFromPrompt(
            $$$"""
            Determine if the code has been approved. If so, respond with a single word: yes.

            History:
            {{$history}}
            """
        );

        // termination function
        var selection_function = KernelFunctionFactory.CreateFromPrompt(
            $$$"""
            Your job is to determine which agent takes the next turn in a conversation.
            State only the name of the participant to take the next turn.
            Choose only from these participants:
            - {{{codevalidator_agent_name}}}.
            - {{{juniordeveloper_agent_name}}}.
            - {{{seniordeveloper_agent_name}}}.

            Follow these rules:
            1) After the user input, it is {{{juniordeveloper_agent_name}}} turn.
            2) After {{{juniordeveloper_agent_name}}}, it's {{{codevalidator_agent_name}}}'s turn.
            3) if the score provided by {{{codevalidator_agent_name}}} is less than 8, it's the {{{seniordeveloper_agent_name}}}'s turn.
            4) After {{{seniordeveloper_agent_name}}} replies, it's {{{codevalidator_agent_name}}}'s turn to validate the code.
            5) Repeat step 3 and 4 until the code is approved.

            History:
            {{$history}}
            """
        );

        var chat = new AgentGroupChat(codevalidator_agent, juniordeveloper_agent, seniordeveloper_agent)
        {
            ExecutionSettings = new()
            {
                TerminationStrategy = termination_strategy(
                    terminate_function: terminate_function,
                    kernel: kernel,
                    agents: [codevalidator_agent]
                ),

                SelectionStrategy = selection_strategy(
                    selection_function: selection_function,
                    kernel: kernel
                )
            }
        };

        var prompt = "Please write a function that takes a string as input and returns the number of words in that string.";
        chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, prompt));
        await foreach (var content in chat.InvokeAsync())
        {
            Console.WriteLine();
            Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
            Console.WriteLine();
        }

    }

    // Helper function to read agent instructions from file
    private static string ReadAgentInstructions(string agentName)
    {
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        return File.ReadAllText(filePath);
    }

    // Helper function to create an agent
    private static ChatCompletionAgent CreateAgent(string name, string instructions, Kernel kernel)
    {
        return new ChatCompletionAgent
        {
            Name = name,
            Instructions = instructions,
            Kernel = kernel
        };
    }

    private static KernelFunction terminate_function()
    {
        return KernelFunctionFactory.CreateFromPrompt(
            $$$"""
            Determine if the ode has been approved. If so, respond with a single word: yes.

            History:
            {{$history}}
            """
        );
    }

    private static KernelFunctionTerminationStrategy termination_strategy(KernelFunction terminate_function, Kernel kernel, ChatCompletionAgent[] agents)
    {
        return new KernelFunctionTerminationStrategy(function: terminate_function, kernel: kernel)
        {
            Agents = agents,
            ResultParser = (result) => result.GetValue<string>()?.Contains("yes", StringComparison.OrdinalIgnoreCase) ?? false,
            HistoryVariableName = "history",
            MaximumIterations = 10
        };
    }
    private static KernelFunctionSelectionStrategy selection_strategy(KernelFunction selection_function, Kernel kernel)
    {
        return new KernelFunctionSelectionStrategy(function: selection_function, kernel: kernel)
        {
            AgentsVariableName = "agents",
            HistoryVariableName = "history"
        };
    }
}