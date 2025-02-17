// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

using LLMSettings;
using LLMClipboardAccess;
using LLMLights;
using Microsoft.SemanticKernel.Agents.OpenAI;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var settings = new AISettings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {settings.AzureOpenAI.ChatModelDeployment}");

        // Create the kernel builder with the pointer to the chat completion service of Azure OpenAI
        var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: settings.AzureOpenAI.ChatModelDeployment,
            endpoint: settings.AzureOpenAI.Endpoint,
            apiKey: settings.AzureOpenAI.ApiKey
        );

        // Add enterprise logging components
        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.None));

        // Build the kernel from the builder that already contains ChatCompletionService + Logging services
        Kernel kernel = builder.Build();

        // Clone the kernel, then add tools to the new one
        Kernel toolKernel = kernel.Clone();
        // Add a plugin (the ClipboardAccess class is defined in its dedicated file LightsPlugin.cs)
        toolKernel.Plugins.AddFromType<ClipboardAccess>("Clipboard");
        // Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
        toolKernel.Plugins.AddFromType<LightsPlugin>("Lights");

        var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };
        var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings);


        // Create the FIRST Chat Completion Agent(s) - Reviewer, with toolKernel
        Console.WriteLine("\nFirst, we'll test just the single Agent \"Reviewer\" (a CHAT COMPLETION agent), until you enter <exit>");
        var reviewer_agent = CreateChatCompletionAgent(agent_name: "Reviewer", kernel: toolKernel, kernelArguments: kernelArguments);
        await ChatWithAgentAsync(agent: reviewer_agent);

        // Create the SECOND OpeAI Assistant Completion Agent(s) - Writer, with simple kernel
        Console.WriteLine("\nAs a second step, we'll test the single Agent (the Writer, an ASSISTANT agent), until you enter <exit>");
        var writer_agent = CreateChatCompletionAgent(agent_name: "Writer", kernel: kernel);

        await ChatWithAgentAsync(agent: writer_agent);

        // "Termination" kernel function that responds "yes" if the last message is satisfactory
        const string TerminationToken = "yes";
        KernelFunction terminationFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Examine the RESPONSE and determine whether the content has been deemed satisfactory.
                If content is satisfactory, respond with a single word without explanation: {{{TerminationToken}}}.
                If specific suggestions are being provided, it is not satisfactory.
                If no correction is suggested, it is satisfactory.

                RESPONSE:
                {{$lastmessage}}
                """,

                safeParameterNames: "lastmessage");


        // "Selection" kernel function that receives the last message and responds the name of the next participant
        KernelFunction selectionFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
            $$$"""
            Examine the provided RESPONSE and choose the next participant.
            State only the name of the chosen participant without explanation.
            Never choose the participant named in the RESPONSE.

            Choose only from these participants:
            - {{{reviewer_agent.Name}}}
            - {{{writer_agent.Name}}}

            Always follow these rules when choosing the next participant:
            - If RESPONSE is user input, it is {{{reviewer_agent.Name}}}'s turn.
            - If RESPONSE is by {{{reviewer_agent.Name}}}, it is {{{writer_agent.Name}}}'s turn.
            - If RESPONSE is by {{{writer_agent.Name}}}, it is {{{reviewer_agent.Name}}}'s turn.

            RESPONSE:
            {{$lastmessage}}
            """,

            safeParameterNames: "lastmessage"
        );

        // history reducer that extracts the last message from the history
        var historyReducer = new ChatHistoryTruncationReducer(targetCount: 1);

        // create the Group Chat Agent
        var groupChatAgent = new AgentGroupChat(reviewer_agent, writer_agent)
        {
            ExecutionSettings = new AgentGroupChatSettings
            {
                TerminationStrategy = new KernelFunctionTerminationStrategy(function: terminationFunction, kernel: kernel)
                {
                    Agents = [reviewer_agent], // Only evaluate for editor's response
                    HistoryReducer = historyReducer, // Save tokens by only including the final response
                    HistoryVariableName = "lastmessage", // The prompt variable name for the history argument.
                    // Customer result parser to determine if the response is "yes":
                    ResultParser = (result) => result.GetValue<string>()?.Contains(TerminationToken, StringComparison.OrdinalIgnoreCase) ?? false,
                    MaximumIterations = 12, // Limit total number of turns
                },

                SelectionStrategy = new KernelFunctionSelectionStrategy(function: selectionFunction, kernel: kernel)
                {
                    InitialAgent = reviewer_agent, // Always start with the editor agent.
                    HistoryReducer = historyReducer, // Save tokens by only including the final response
                    HistoryVariableName = "lastmessage", // The prompt variable name for the history argument.

                    // Returns the entire result value as a string
                    // "nullish coalescing" (??) operator returns the left value if not null/undefined; otherwise it returns the value on the right
                    ResultParser = (result) => result.GetValue<string>() ?? reviewer_agent.Name
                }
            }
        };

        Console.WriteLine("\nAs a third and last step, we'll test the Agent Group Chat");
        await ChatWithAgentAsync(agent: groupChatAgent);
    }


    // Helper function to read agent instructions from file
    private static string ReadAgentInstructions(string agentName)
    {
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        return File.ReadAllText(filePath);
    }

    // Helper function to create an agent
    private static ChatCompletionAgent CreateChatCompletionAgent(string agent_name, Kernel kernel, KernelArguments? kernelArguments = null)
    {
        return new ChatCompletionAgent
        {
            Name = agent_name,
            Instructions = ReadAgentInstructions(agent_name),
            Kernel = kernel,
            Arguments = kernelArguments ?? new KernelArguments() // Providing a default value if kernelArguments is null
        };
    }

    // Helper function to create an Assistant agent
    private static async Task<OpenAIAssistantAgent> CreateAssistantAgentAsync(
        string agent_name, Kernel kernel, OpenAIClientProvider clientProvider, string deploymentName)
    {
        var assistantAgent =
            await OpenAIAssistantAgent.CreateAsync(
                clientProvider: clientProvider,
                definition: new OpenAIAssistantDefinition(deploymentName)
                {
                    Name = agent_name,
                    Instructions = ReadAgentInstructions(agent_name),
                    EnableCodeInterpreter = true,
                    EnableFileSearch = false
                },
                kernel: kernel // empty kernel, with no associated plugins nor services
                );

        return assistantAgent;
    }

    private static async Task ChatWithAgentAsync(object agent)
    {
        // Create a history store the conversation
        var history = new ChatHistory();

        // Initiate the chat (either single-agent or group chat)
        string? userInput;

        bool exit_chat = false;
        do
        {
            // Collect user input
            Console.Write("User > ");

            userInput = Console.ReadLine();

            exit_chat = (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase));

            // Check if userInput is not null before adding it to the chat history
            if (!exit_chat)
            {
                if (agent is ChatCompletionAgent chatAgent)
                {
                    history.AddUserMessage(userInput);
                    await foreach (ChatMessageContent response in chatAgent.InvokeAsync(history))
                    {
                        Console.WriteLine($"{response.Content}");
                        // Add the message from the agent to the chat history
                        history.AddMessage(response.Role, response.Content ?? string.Empty);
                    }
                }
                else if (agent is AgentGroupChat groupAgent)
                {
                    groupAgent.AddChatMessage(new ChatMessageContent(AuthorRole.User, userInput));
                    await foreach (ChatMessageContent response in groupAgent.InvokeAsync())
                    //await foreach (StreamingChatMessageContent response in groupAgent.InvokeStreamingAsync())
                    {
                        // Add the message from the agent to the chat history
                        Console.WriteLine($"\n#### >>> {response.Role} - {response.AuthorName ?? "*"}:\n'{response.Content}'");
                        //Console.Write(response.Content);
                    }
                    exit_chat = exit_chat || groupAgent.IsComplete;
                }
            }
        } while (!exit_chat);
    }
}