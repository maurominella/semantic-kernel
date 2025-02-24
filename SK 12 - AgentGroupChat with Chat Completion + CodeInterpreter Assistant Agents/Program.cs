// Copyright (c) Microsoft. All rights reserved.

using LLMSettings;
using Microsoft.SemanticKernel;

using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.Agents.Chat;

using OpenAI.Files;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets
        var aiSettings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}");

        // Create the kernel builder object that encapsulates the pointer to Azure OpenAI
        IKernelBuilder builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: aiSettings.AzureOpenAI.ChatModelDeployment,
            endpoint: aiSettings.AzureOpenAI.Endpoint,
            apiKey: aiSettings.AzureOpenAI.ApiKey
        );

        // Add enterprise logging components
        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

        // Use above builder to create the kernel so to inlcude a) LLM pointer b) logging service
        Kernel kernel = builder.Build();

        // Create the Chat Completion Agent: "Joker", with simple kernel
        ChatCompletionAgent? joker_agent = await CreateAgentAsync(
            agent_type: "chatcompletion_agent",
            agent_name: "Joker",
            kernel: kernel) as ChatCompletionAgent;

        // Test the Joker agent
        Console.WriteLine("\n\nFirst, we'll test just the single Agent \"Joker\" (a CHAT COMPLETION agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: joker_agent);


        // Create the Assistant Agent: "Statistician", with Code Interpreter in three steps

        // Step 1: OpenAIClientProvider is used for the Agent Definition as well as file-upload
        var clientProviderForAzure = OpenAIClientProvider.ForAzureOpenAI(
            apiKey: new System.ClientModel.ApiKeyCredential(aiSettings.AzureOpenAI.ApiKey),
            endpoint: new Uri(aiSettings.AzureOpenAI.Endpoint));

        // Step 2: Create a pointer to the file client provider, to both upload and download files
        OpenAIFileClient fileClient = clientProviderForAzure.Client.GetOpenAIFileClient();

        // Step 3: Create the OpenAI Assistant Agent
        OpenAIAssistantAgent? statistician_agent = await CreateAgentAsync(
            agent_type: "assistant_agent",
            agent_name: "Statistician",
            kernel: kernel,
            deploymentName: aiSettings.AzureOpenAI.ChatModelDeployment,
            clientProvider: clientProviderForAzure,
            enableCodeInterpreter: true) as OpenAIAssistantAgent;

        // Test the Statistician agent
        Console.WriteLine("\n\nAs a second step, we'll test just the single \"Statistician\" (an ASSISTANT agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: statistician_agent, fileClient: fileClient);


        // Create the Reviewer ChatCompletion Agent: "Reviewer", with simple kernel
        ChatCompletionAgent? reviewer_agent = await CreateAgentAsync(
            agent_type: "chatcompletion_agent",
            agent_name: "Reviewer",
            kernel: kernel) as ChatCompletionAgent;

        Console.WriteLine("Reviewer agent was created");


        // AGENT GROUP CHAT PREPARATION

        // "Termination" kernel function that responds "yes" if the last message is satisfactory
        const string TerminationToken = "yes";
        KernelFunction terminationFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Examine the RESPONSE and determine whether the content has been deemed satisfactory.
                If content is satisfactory, respond with a single word without explanation: {{{TerminationToken}}}.
                If specific suggestions are being provided, it is not satisfactory.
                If it's made of a single word, it's not satisfactory.
                If it has more than one word and no correction is suggested, it is satisfactory.

                RESPONSE:
                {{$lastmessage}}
                """,

                safeParameterNames: "lastmessage");


        // "Selection" kernel function that receives the last message and responds the name of the next participant
        KernelFunction selectionFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Examine the provided RESPONSE and generate **EXCLUSIVELY A SINGLE WORD** with the name of the next participant.

                Choose only from these participants:
                - {{{joker_agent.Name}}}
                - {{{statistician_agent.Name}}}
                - {{{reviewer_agent.Name}}}

                Always follow these rules when choosing the next participant:
                - If RESPONSE is user input, it is {{{reviewer_agent.Name}}}'s turn.
                - If RESPONSE is by {{{joker_agent.Name}}}, it is {{{statistician_agent.Name}}}'s turn.
                - If RESPONSE is by {{{statistician_agent.Name}}}, it is {{{reviewer_agent.Name}}}'s turn.
                - If RESPONSE is by {{{reviewer_agent.Name}}}, it is {{{joker_agent.Name}}}'s turn.

                RESPONSE:
                {{$lastmessage}}
                """,

                safeParameterNames: "lastmessage");


        // history reducer that extracts the last message from the history
        var historyReducer = new ChatHistoryTruncationReducer(targetCount: 1);


        // // AGENT GROUP CHAT CREATION
        var groupChatAgent = new AgentGroupChat(joker_agent, statistician_agent, reviewer_agent)
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
                    MaximumIterations = 20, // Limit total number of turns
                },

                SelectionStrategy = new KernelFunctionSelectionStrategy(function: selectionFunction, kernel: kernel)
                {
                    InitialAgent = reviewer_agent, // Always start with the editor agent.
                    HistoryReducer = historyReducer, // Save tokens by only including the final response
                    HistoryVariableName = "lastmessage", // The prompt variable name for the history argument.

                    // Returns the entire result value as a string
                    // "nullish coalescing" (??) operator returns the left value if not null/undefined; otherwise it returns the value on the right
                    ResultParser = (result) => result.GetValue<string>() ?? statistician_agent.Name
                }
            }
        };

        Console.WriteLine("\n\nAs final step, test the Agent Group Chat");
        await ChatWithAgentAsync(agent: groupChatAgent, fileClient: fileClient);
    }


    // HELPER FUNCTIONS


    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agentName)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }


    // Helper function to create an Assistant agent
    private static async Task<object> CreateAgentAsync(
        string agent_type, string agent_name, Kernel kernel, OpenAIClientProvider? clientProvider = null, string? deploymentName = null,
        bool enableCodeInterpreter = false, bool enableFileSearch = false, KernelArguments? kernelArguments = null)
    {
        object? agent = null;

        if (agent_type == "chatcompletion_agent")
        {
            agent = new ChatCompletionAgent
            {
                Name = agent_name,
                Instructions = await ReadAgentInstructionsAsync(agent_name),
                Kernel = kernel,
                Arguments = kernelArguments ?? new KernelArguments() // Providing a default value if kernelArguments is null
            };
        }
        else if (agent_type == "assistant_agent")
        {
            agent =
                await OpenAIAssistantAgent.CreateAsync(
                    clientProvider: clientProvider,
                    definition: new OpenAIAssistantDefinition(deploymentName)
                    {
                        Name = agent_name,
                        Instructions = await ReadAgentInstructionsAsync(agent_name),
                        EnableCodeInterpreter = enableCodeInterpreter,
                        EnableFileSearch = enableFileSearch
                    },
                    kernel: kernel, // empty kernel, with no associated plugins nor services
                    defaultArguments: kernelArguments ?? new KernelArguments()
                );
        }

        return agent;
    }


    // Helper function to remove duplicates from a string list, when it's built by a streaming function
    private static List<string> RemoveDuplicates(List<string> fileIds)
    {
        // Using HashSet to remove duplicates
        var uniqueFileIds = new HashSet<string>(fileIds);

        // Converting HashSet back to List
        return uniqueFileIds.ToList();
    }


    // Helper function to automate DownloadFileContentAsync
    private static async Task DownloadResponseImageAsync(OpenAIFileClient client, List<string> fileIds)
    {
        if (fileIds.Count > 0)
        {
            Console.WriteLine();
            foreach (string fileId in fileIds)
            {
                await DownloadFileContentAsync(client, fileId, launchViewer: true);
            }
        }
    }


    // Helper function to download content from a single file
    private static async Task DownloadFileContentAsync(OpenAIFileClient client, string fileId, bool launchViewer = false)
    {
        OpenAIFile fileInfo = client.GetFile(fileId);
        if (fileInfo.Purpose == FilePurpose.AssistantsOutput)
        {
            string filePath =
                Path.Combine(
                    Path.GetTempPath(),
                    Path.GetFileName(Path.ChangeExtension(fileInfo.Filename, ".png")));

            if (!File.Exists(filePath))
            {
                BinaryData content = await client.DownloadFileAsync(fileId);
                await using FileStream fileStream = new(filePath, FileMode.CreateNew);
                await content.ToStream().CopyToAsync(fileStream);
                Console.WriteLine($"File saved to: {filePath}.");
            }

            if (launchViewer)
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C start {filePath}"
                    });
            }
        }
    }


    // Single Chat function for all kinds of agents
    private static async Task ChatWithAgentAsync(object agent, OpenAIFileClient? fileClient = null)
    {
        string? userInput;
        bool exit_chat = false;

        do
        {
            Console.Write("User > ");
            userInput = Console.ReadLine();

            // Check if userInput is not null or EXIT
            exit_chat = string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase);

            if (!exit_chat)
            {

                // check if it's a CHAT COMPLETION agent
                if (agent is ChatCompletionAgent chatAgent)
                {
                    var history = new ChatHistory();
                    history.AddUserMessage(userInput);
                    await foreach (ChatMessageContent response in chatAgent.InvokeAsync(history))
                    {
                        Console.WriteLine($"{response.Content}");
                        // Add the message from the agent to the chat history
                        history.AddMessage(response.Role, response.Content ?? string.Empty);
                    }
                }

                // check if it's an ASSISTANT agent
                else if (agent is OpenAIAssistantAgent assistantAgent)
                {
                    List<string> fileIds = [];
                    string threadId = await assistantAgent.CreateThreadAsync();
                    await assistantAgent.AddChatMessageAsync(threadId, new ChatMessageContent(AuthorRole.User, userInput));

                    try
                    {
                        bool isCode = false;
                        await foreach (StreamingChatMessageContent response in assistantAgent.InvokeStreamingAsync(threadId))
                        {
                            if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                            {
                                Console.WriteLine();
                                isCode = !isCode;
                            }
                            // Display response.
                            Console.Write($"{response.Content}");

                            // Capture file IDs for downloading
                            fileIds.AddRange(response.Items.OfType<StreamingFileReferenceContent>().Select(item => item.FileId));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                    }

                    fileIds = RemoveDuplicates(fileIds);
                    Console.WriteLine();

                    // Download any images referenced in the response
                    await DownloadResponseImageAsync(fileClient, fileIds);

                    fileIds.Clear();
                }

                // check if it's a GROUP CHAT agent
                else if (agent is AgentGroupChat groupAgent)
                {
                    var author_name = ""; // used in the streaming to check when the author changes
                    List<string> fileIds = [];
                    groupAgent.AddChatMessage(new ChatMessageContent(AuthorRole.User, userInput));
                    await foreach (StreamingChatMessageContent response in groupAgent.InvokeStreamingAsync())
                    //await foreach (ChatMessageContent response in groupAgent.InvokeAsync())
                    {
                        // Add the message from the agent to the chat history
                        //Console.WriteLine($"# {response.Role} - {response.AuthorName ?? "*"}: '{response.Content}'");
                        if (response.AuthorName != author_name)
                        {
                            Console.WriteLine($"\n\n### New turn: {response.Role} - {response.AuthorName ?? "*"}:\n");
                            author_name = response.AuthorName;
                        }
                        Console.Write(response.Content);

                        // Capture file IDs for downloading
                        fileIds.AddRange(response.Items.OfType<StreamingFileReferenceContent>().Select(item => item.FileId));
                    }
                    fileIds = RemoveDuplicates(fileIds);
                    Console.WriteLine();

                    // Download any images referenced in the response
                    await DownloadResponseImageAsync(fileClient, fileIds);

                    fileIds.Clear();
                    Console.WriteLine();
                    exit_chat = exit_chat || groupAgent.IsComplete;
                }
            }
        } while (!exit_chat);
    }

}