// Copyright (c) Microsoft. All rights reserved.

// Last update: June 6th, 2025

// Created with: dotnet new console -n "SK 08 - ChatCompletion + Assistant + AI Foundry agents (no group)_NEW" --framework net9.0

/*
Supporting documentation:
- Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
- Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
- AzureAIAgent Foundry GA Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp
- Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
- Microsoft.SemanticKernel.Agents Namespace (prerelease): https://learn.microsoft.com/it-it/dotnet/api/microsoft.semantickernel.agents?view=semantic-kernel-dotnet
- Create Agent with Bing Grounding: https://www.nuget.org/packages/Azure.AI.Agents.Persistent/1.0.0#create-agent-with-bing-grounding

Features included:
- Azure.AI.Agents.Persistent library
- Microsoft.SemanticKernel.Agents.AzureAI library
- Bing Grounding tool
- Multiple types of agents
- Single function for agent creation
- Single function for chatting with agents
*/

#region Libraries and Namespaces
using Azure;

// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.5" />
// dotnet add package Microsoft.Extensions.Logging.Console --> <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.5" />
using Microsoft.Extensions.Logging; // needed for LogLevel

// dotnet add package Microsoft.Extensions.DependencyInjection --> <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.5" />
using Microsoft.Extensions.DependencyInjection; // needed for AddLogging

// dotnet add package Azure.AI.Agents.Persistent --> <PackageReference Include="Azure.AI.Agents.Persistent" Version="1.0.0" />
using Azure.AI.Agents.Persistent; // needed for PersistentAgentsClient, PersistentAgent, PersistentAgentThread, PersistentThreadMessage, ThreadRun, RunStatus, MessageRole, MessageContent, MessageTextContent, MessageImageFileContent, BingGroundingToolDefinition, BingGroundingSearchToolParameters, BingGroundingSearchConfiguration

// dotnet add package Microsoft.SemanticKernel.Agents.Core --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.55.0" />
using Microsoft.SemanticKernel.Agents; // needed for ChatCompletion
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.55.0-preview" />
using Microsoft.SemanticKernel.Agents.AzureAI;

// JUST FOR ASSISTANT API'S
// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --><PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.55.0-preview" />


// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;
using Microsoft.SemanticKernel.ChatCompletion;

using AIPlugins;
using Microsoft.SemanticKernel.Agents.OpenAI;
using OpenAI.Assistants;
using Azure.AI.OpenAI;
using OpenAI.Files;
using OpenAI.VectorStores; // contains the LightsPlugin class

namespace LLMSettings;

#endregion

internal class Program
{
    private static string? s_aiagent_id = null;
    private static async Task Main(string[] args)
    {
        #region Environment Configuration
        Console.WriteLine("Application starts");
        // Load configuration from environment variables or user secrets.
        var ai_settings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {ai_settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {ai_settings.AzureOpenAI.ChatModelDeployment}\n" +
        $"PROJECT_ENDPOINT: {ai_settings.AzureOpenAI.ProjectEndpoint}");
        #endregion

        #region Semantic Kernel ChatCompletion Agent

        Console.Write("\n\n+++++++++ CHAT COMPLETION AGENT +++++++++\n");

        ChatCompletionAgent? sk_chatcompletion_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "sk_chatcompletion_agent",
            instructions: "You are a clever chat completion agent"
            ) as ChatCompletionAgent;

        // chat with the agent
        try
        {
            await ChatWithAgentAsync(agent: sk_chatcompletion_agent);
        }
        finally
        {
            // deallocate the agent
            Console.WriteLine($"Deallocating agent {sk_chatcompletion_agent.Id} that is just an instance of ChatCompletionAgent...");
            sk_chatcompletion_agent = null;
        }

        #endregion

        #region Semantic Kernel Assistant API Agent with Plugin, CodeInterpreter and FileClient features
        Console.Write("\n\n+++++++++ ASSISTANT AGENT +++++++++\n");

        // Identify the files to search in, with the openai assistant search feature
        string[] s_files_to_search_in =
        [
            "data/search_files/trailmaster_product_info_1.md",
        ];

        // Identify the files to work with with openai assistant code interpreter
        string[] s_files_to_work_with =
        [
            "data/codeinterpreter_files/turbines.xlsx",
        ];

        OpenAIAssistantAgent? sk_assistant_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_assistantapi_agent",
            agent_name: "sk_assistantapi_agent",
            instructions: "You are a clever openai assistant api agent",
            s_files_to_search_in: s_files_to_search_in,
            s_files_to_work_with: s_files_to_work_with
            ) as OpenAIAssistantAgent;


        // chat with the agent
        try
        {
            await ChatWithAgentAsync(agent: sk_assistant_agent);
        }
        finally
        {
            // deallocate the agent
            Console.WriteLine($"Deleting Assistant agent {sk_assistant_agent.Id}...");
            await sk_assistant_agent.Client.DeleteAssistantAsync(sk_assistant_agent.Id);
        }

        #endregion

        #region Semantic Kernel AI Foundry Agent
        Console.Write("\n\n+++++++++ AI FOUNDRY AGENT +++++++++\n");
        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        s_aiagent_id = Console.ReadLine();

        PersistentAgentsClient aiproject_client = AzureAIAgent.CreateAgentsClient(ai_settings.GetVariable("PROJECT_ENDPOINT"), new AzureCliCredential());

        // Microsoft.SemanticKernel.Agents.AzureAI    
        PersistentAgent? sk_ai_agent = await GenericCreateAgentAsync(
            agent_type: "sk_azure_aifoundry_agent",
            agent_name: "AnimalPicker",
            aiagent_id: s_aiagent_id,
            aiproject_client: aiproject_client,
            ai_settings: ai_settings
            ) as PersistentAgent;

        try
        {
            await ChatWithAgentAsync(
                sk_ai_agent,
                aiproject_client: aiproject_client);
        }
        finally
        {
            Console.WriteLine($"\nDeleting agent {sk_ai_agent.Name}({sk_ai_agent.Id})...");
            await aiproject_client.Administration.DeleteAgentAsync(agentId: sk_ai_agent.Id);
        }
        #endregion
    }


    // Single Chat function for all kinds of agents
    private static async Task<object> GenericCreateAgentAsync(
        string agent_type, string? agent_name = null, PersistentAgentsClient? aiproject_client = null, string? aiproject_endpoint = null,
        string[]? s_files_to_search_in = null, string[]? s_files_to_work_with = null,
        AISettings? ai_settings = null, string? instructions = null, string? aiagent_id = null)
    {

        object? agent = null;

        // Provided instructions take the precedence of the ones stored in the agent's file
        if (string.IsNullOrWhiteSpace(instructions))
        {
            instructions = await ReadAgentInstructionsAsync(agent_name: agent_name);
        }

        if (agent_type == "sk_chatcompletion_agent")
        {
            // Create the kernel builder with the pointer to Azure OpenAI
            Microsoft.SemanticKernel.IKernelBuilder builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
                deploymentName: ai_settings.AzureOpenAI.ChatModelDeployment,
                endpoint: ai_settings.AzureOpenAI.Endpoint,
                apiKey: ai_settings.AzureOpenAI.ApiKey);

            // Use the kernel builder to add enterprise components (for logging, in this case)
            // Requires Microsoft.Extensions.Logging and Microsoft.Extensions.DependencyInjection
            builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.None));

            // Build the kernel
            Kernel kernel = builder.Build();

            // add the plugin to the kernel
            Microsoft.SemanticKernel.KernelPlugin lights_plugin = kernel.Plugins.AddFromType<LightsPlugin>("Lights");

            // Enable planning
            // if "pure" OpenAI, please use      OpenAIPromptExecutionSettings
            // in Azure OpenAI, we have     AzureOpenAIPromptExecutionSettings
            var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
            var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings) // optional            
            {
                { "repository", "microsoft/semantic-kernel" }
            };

            var sk_chatcompletion_agent = new ChatCompletionAgent
            {
                Name = agent_name,
                Instructions = instructions,
                Kernel = kernel,
                Arguments = kernelArguments ?? new KernelArguments() // Provide a default value if kernelArguments is null
            };

            agent = sk_chatcompletion_agent;
        }

        else if (agent_type == "sk_assistantapi_agent")
        {
            // Instantiation of the Assistant API Client for Azure OpenAI 
            AzureOpenAIClient azure_openai_client = OpenAIAssistantAgent.CreateAzureOpenAIClient(
                credential: new AzureCliCredential(), endpoint: new Uri(ai_settings.AzureOpenAI.Endpoint));

            // Get the File Client
            OpenAIFileClient file_client = azure_openai_client.GetOpenAIFileClient();

            // Upload the files to search in, with the openai assistant search feature
            var s_openaifiles_to_search_in = new List<OpenAIFile>();
            foreach (string f in s_files_to_search_in)
            {
                OpenAIFile fileInfo = await file_client.UploadFileAsync(f, FileUploadPurpose.Assistants);
                s_openaifiles_to_search_in.Add(fileInfo);
                Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
            }

            // Upload the files to work with with openai assistant code interpreter
            var s_id_files_to_work_with = new List<string>();
            foreach (string f in s_files_to_work_with)
            {
                OpenAIFile fileInfo = await file_client.UploadFileAsync(f, FileUploadPurpose.Assistants);
                s_id_files_to_work_with.Add(fileInfo.Id);
                Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
            }

            // Get the VectorStore Client
            VectorStoreClient storeClient = azure_openai_client.GetVectorStoreClient();

            // Create a Vector Store and add the files to it (only the ones to search in)
            string storeId = (await storeClient.CreateVectorStoreAsync(waitUntilCompleted: true)).VectorStoreId;
            Console.WriteLine($"New Vector Store created with Id = <{storeId}>");
            foreach (OpenAIFile file in s_openaifiles_to_search_in) // if you want **ALL** files: (await fileClient.GetFilesAsync()).Value)
            {
                Console.WriteLine($"Adding new file <Id: {file.Id}, Name: {file.Filename}> to the <{storeId}> vector store...");
                await storeClient.AddFileToVectorStoreAsync(storeId, file.Id, waitUntilCompleted: true);
            }

            // Get the ASSISTANT API CLIENT
            AssistantClient assistant_client = azure_openai_client.GetAssistantClient();

            // Create an ASSISTANT (or ASSISTANT DEFINITION)
            Assistant assistant = await assistant_client.CreateAssistantAsync(
                modelId: ai_settings.AzureOpenAI.ChatModelDeployment, // deployment name
                name: "mauromi's assistant",
                description: "My first assistant",
                instructions: "You are a clever assistant",
                enableCodeInterpreter: true,
                codeInterpreterFileIds: s_id_files_to_work_with,
                enableFileSearch: true,
                vectorStoreId: storeId); // includes s_opeaifiles_to_search_in
            Console.WriteLine($"Created new Assistant <{assistant.Name}> with Id <{assistant.Id}>");

            // Create an assistant AGENT
            var sk_assistantapi_agent = new OpenAIAssistantAgent(definition: assistant, client: assistant_client);

            // Add a plugin to the assistant agent
            sk_assistantapi_agent.Kernel.Plugins.AddFromType<LightsPlugin>("Lights");

            agent = sk_assistantapi_agent;
        }


        else if (agent_type == "sk_azure_aifoundry_agent")
        {

            BingGroundingToolDefinition bingGroundingTool = new(
                bingGrounding: new BingGroundingSearchToolParameters(
                    [new BingGroundingSearchConfiguration(connectionId: ai_settings.GetVariable("BING_CONNECTION_ID"))]
                )
            );

            PersistentAgent sk_ai_agent;
            if (string.IsNullOrWhiteSpace(aiagent_id))
            {
                sk_ai_agent = await aiproject_client.Administration.CreateAgentAsync(
                    model: ai_settings.AzureOpenAI.ChatModelDeployment,
                    name: agent_name,
                    description: agent_name,
                    instructions: instructions,
                    tools: [bingGroundingTool]
                );

            }
            else
            {
                // sk_ai_agent = await agentsClient.Administration.GetAgentAsync(agentId: aiagent_id, context: null);
                var sk_ai_agent_response = await aiproject_client.Administration.GetAgentAsync(agentId: aiagent_id, context: null);
                sk_ai_agent = null;
            }

            agent = sk_ai_agent;
        }

        return agent;
    }


    // Single Chat function for all kinds of agents
    private static async Task ChatWithAgentAsync(object agent, PersistentAgentsClient? aiproject_client = null)
    {
        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        if (agent is ChatCompletionAgent sk_chatcompletion_agent)
        {
            Console.Write("\nWelcome to the ChatCompletionAgent! Ask me anything, or type 'EXIT' to quit.\n");

            // Create a history store the conversation, however use ChatHistoryAgentThread instead of ChatHistory, which is deprecated
            var sk_chatcompletionagent_thread = new ChatHistoryAgentThread();

            do
            {
                Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- how many feets are there in a mile? (e.g. normal Chat Completion, to show how the history is stored),
- toggle the chandelier and tell me the status of all lights (e.g. Plugin usage),
- tell me a joke with no less than 200 words (e.g. normal Chat Completion, to show streaming features),

Your turn > ");
                // Collect user input
                user_input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    exit_chat = true;
                    break;
                }

                Console.WriteLine("Chatting with the ChatCompletionAgent...");
                var message = new ChatMessageContent(AuthorRole.User, user_input);

                // await foreach (ChatMessageContent response in sk_chatcompletion_agent.InvokeAsync(message: message, thread: sk_chatcompletionagent_thread))
                await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(
                    message: message, thread: sk_chatcompletionagent_thread))
                {
                    Console.Write($"{response.Content}");
                }

                Console.Write($"\n\nThere are {sk_chatcompletionagent_thread.ChatHistory.Count()} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive. > ");
                var clear_history = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
                {
                    await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(
                        message: new ChatMessageContent(AuthorRole.User, "Reset lights status"),
                        thread: sk_chatcompletionagent_thread))
                    {
                        //Console.Write($"{response.Content}");
                    }

                    sk_chatcompletionagent_thread.ChatHistory.Clear();
                }

            } while (!exit_chat);
        }


        else if (agent is OpenAIAssistantAgent sk_assistantapi_agent)
        {
            Console.Write("\nWelcome to the OpenAIAssistantAgent! Ask me anything, or type 'EXIT' to quit.\n");

            // Here we just define (without creating) the AgentThread variable
            // An AgentThread will be started and returned as part of the response
            AgentThread? agent_thread = null;

            Console.Write("\nWelcome to the OpenAI Assistant agent! Ask me anything, or type 'EXIT' to quit.\n");
            do
            {
                Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- tell me a joke with no less than 200 words (e.g. normal Chat Completion),
- toggle the chandelier and tell me the status of all lights (e.g. Plugin usage),
- what's the Season Rating for the TrailMaster X4 Tent? (e.g. Assistant API's File Search),
- use turbines.xlsx to find the earliest Maintenance_Date among the turbine with highest voltage (e.g. Assistant API's Code Interpreter),

Your turn > ");

                // Collect user input
                user_input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    exit_chat = true;
                    break;
                }

                // Generate the agent response(s)
                await foreach (AgentResponseItem<StreamingChatMessageContent> response in sk_assistantapi_agent.InvokeStreamingAsync( // streaming version
                // await foreach (AgentResponseItem<ChatMessageContent> response in assistant_agent.InvokeAsync( // non streaming version
                    new ChatMessageContent(AuthorRole.User, user_input), thread: agent_thread))
                {
                    // Process agent response(s)...
                    Console.Write($"{response.Message.Content}");
                    agent_thread = response.Thread;
                }

                Console.Write($"\n\nEnter 'Y' if you want to clear the history, or anything else to keep the thread and its messages alive > ");
                var clear_history = Console.ReadLine();
                // Delete the thread if no longer needed
                if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
                {
                    // await agent_thread.DeleteAsync();
                    agent_thread = null;
                }

            } while (!exit_chat);
        }


        else if (agent is PersistentAgent sk_ai_agent)
        {
            Console.Write("\nWelcome to the AI Foundry Agent (PersistentAgent object)! Ask me anything, or type 'EXIT' to quit.\n");
            do
            {
                Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- what is the biggest insect? (e.g. grounding with Bing Search),
- what's the animal of the year for 2024? (e.g. grounding with Bing Search),
- what's the biggest mammal? (e.g. grounding with Bing Search),
- qual è l'animale più grande, che però non nuota? (e.g. grounding with Bing Search),

Your turn > ");

                // Collect user input
                user_input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    exit_chat = true;
                    break;
                }

                Console.WriteLine("Chatting with the AI Foundry agent...");
                PersistentAgentThread thread = await aiproject_client.Threads.CreateThreadAsync();
                PersistentThreadMessage message = await aiproject_client.Messages.CreateMessageAsync(
                    thread.Id,
                    Azure.AI.Agents.Persistent.MessageRole.User, user_input);

                Azure.AI.Agents.Persistent.ThreadRun run = await aiproject_client.Runs.CreateRunAsync(
                    thread.Id,
                    sk_ai_agent.Id,
                    additionalInstructions: "Please address the user as Jane Doe. The user has a premium account.");

                // Once the run has started, it should then be polled until it reaches a terminal status:
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    run = await aiproject_client.Runs.GetRunAsync(thread.Id, run.Id);
                }
                while (run.Status == Azure.AI.Agents.Persistent.RunStatus.Queued
                    || run.Status == Azure.AI.Agents.Persistent.RunStatus.InProgress);

                // Retrieve the messages from the run: assuming the run successfully completed, listing messages from the thread that was run will now reflect new information added by the agent


                AsyncPageable<PersistentThreadMessage> messages =
                    aiproject_client.Messages.GetMessagesAsync(
                        threadId: thread.Id, order: ListSortOrder.Ascending);

                await foreach (PersistentThreadMessage threadMessage in messages)
                {
                    Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
                    foreach (Azure.AI.Agents.Persistent.MessageContent contentItem in threadMessage.ContentItems)
                    {
                        if (contentItem is MessageTextContent textItem)
                        {
                            Console.Write(textItem.Text);
                        }
                        else if (contentItem is MessageImageFileContent imageFileItem)
                        {
                            Console.Write($"<image from ID: {imageFileItem.FileId}");
                        }
                        Console.WriteLine();
                    }
                }
            } while (!exit_chat);

        }
    }


    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agent_name)
    {
        string filePath = Path.Combine("agents", $"{agent_name}.txt");

        if (!File.Exists(filePath))
        {
            return "You are a clever agent";
        }

        try
        {
            return await File.ReadAllTextAsync(filePath);
        }
        catch (IOException ex)
        {
            return $"IO error occurred: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Access error occurred: {ex.Message}";
        }
    }

}