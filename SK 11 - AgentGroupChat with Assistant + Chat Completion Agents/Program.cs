// Copyright (c) Microsoft. All rights reserved.

using LLMSettings;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.Identity;
using OpenAI.Files;
using System.Diagnostics;

// Copyright (c) Microsoft. All rights reserved.

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");
        // Load configuration from environment variables or user secrets.

        var settings = new AISettings();
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


        // Create the Chat Completion Agent: "Joker", with simple kernel
        var joker_agent = CreateChatCompletionAgent(agent_name: "Joker", kernel: kernel);
        Console.WriteLine("\n\nFirst, we'll test just the single Agent \"Joker\" (a CHAT COMPLETION agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: joker_agent);


        // Create the Assistant Agent: "Statistician", with Code Interpreter
        // OpenAIClientProvider will be used for the Agent Definition as well as file-upload
        var clientProviderForAzure = OpenAIClientProvider.ForAzureOpenAI(
            credential: new AzureCliCredential(),
            endpoint: new Uri(settings.AzureOpenAI.Endpoint));

        // create a pointer to the file client provider, to both upload and download files
        OpenAIFileClient fileClient = clientProviderForAzure.Client.GetOpenAIFileClient();

        // Create the OpenAI Assistant Agent
        OpenAIAssistantAgent statistician_agent = await CreateAssistantAgentAsync(
            agent_name: "Statistician",
            kernel: kernel,
            deploymentName: settings.AzureOpenAI.ChatModelDeployment,
            clientProvider: clientProviderForAzure);

        Console.WriteLine("\n\nNow, we'll test just the single \"Statistician\" (an ASSISTANT agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: statistician_agent, fileClient: fileClient);
    }


    // HELPER FUNCTIONS


    // Read agent instructions from file
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


    // Helper function to create a Chat Completion Agent
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


    // Helper function to create an Assistant Agent
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


    private static List<string> RemoveDuplicates(List<string> fileIds)
    {
        // Using HashSet to remove duplicates
        var uniqueFileIds = new HashSet<string>(fileIds);

        // Converting HashSet back to List
        return uniqueFileIds.ToList();
    }

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

    private static async Task ChatWithAgentAsync(object agent, OpenAIFileClient? fileClient = null)
    {
        string? userInput;
        bool exit_chat = false;
        var history = new ChatHistory();

        do
        {
            Console.Write("User > ");
            List<string> fileIds = [];
            userInput = Console.ReadLine();

            // Check if userInput is not null or EXIT
            exit_chat = string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase);

            if (!exit_chat)
            {
                // check if it's an ASSISTANT agent
                if (agent is OpenAIAssistantAgent assistantAgent)
                {
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
                        fileIds = RemoveDuplicates(fileIds);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        break;
                    }
                    Console.WriteLine();

                    // Download any images referenced in the response
                    await DownloadResponseImageAsync(fileClient, fileIds);

                    fileIds.Clear();
                }

                // check if it's a CHAT COMPLETION agent
                else if (agent is ChatCompletionAgent chatAgent)
                {
                    history.AddUserMessage(userInput);
                    await foreach (ChatMessageContent response in chatAgent.InvokeAsync(history))
                    {
                        Console.WriteLine($"{response.Content}");
                        // Add the message from the agent to the chat history
                        history.AddMessage(response.Role, response.Content ?? string.Empty);
                    }
                }

                // check if it's a GROUP CHAT agent
                else if (agent is AgentGroupChat groupAgent)
                {
                    groupAgent.AddChatMessage(new ChatMessageContent(AuthorRole.User, userInput));
                    await foreach (ChatMessageContent response in groupAgent.InvokeAsync())
                    {
                        // Add the message from the agent to the chat history
                        Console.WriteLine($"# {response.Role} - {response.AuthorName ?? "*"}: '{response.Content}'");
                    }
                    exit_chat = exit_chat || groupAgent.IsComplete;
                }
            }
        } while (!exit_chat);
    }
}