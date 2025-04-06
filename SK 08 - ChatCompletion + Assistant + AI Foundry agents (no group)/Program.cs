// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
// BING: https://learn.microsoft.com/en-us/azure/ai-services/agents/how-to/tools/bing-grounding?tabs=csharp&pivots=code-example#step-2-create-an-agent-with-the-grounding-with-bing-search-tool-enabled
// Migration guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide?pivots=programming-language-csharp
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet

// dotnet new console -n "SK 08 - ChatCompletion + Assistant + AI Foundry agents (no group)" --framework net9.0

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.45.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.45.0-preview" />
using Microsoft.SemanticKernel.Agents.AzureAI;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.45.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.Core --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.45.0" />
using Microsoft.SemanticKernel.Agents; // needed for ChatCompletion

// dotnet add package Azure.AI.Projects --version 1.0.0-beta.3 --> <PackageReference Include="Azure.AI.Projects" Version="1.0.0-beta.3" />
using Azure.AI.Projects; // Microsoft.SemanticKernel.Agents.AzureAI 1.44.0-preview requires Azure.AI.Projects (= 1.0.0-beta.3)

// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.13.2" />
using Azure.Identity;

// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.3" />
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using MyApp.Plugins;
using Azure.AI.OpenAI;
using OpenAI.Files;
using OpenAI.Assistants;
using System.Diagnostics;

namespace LLMSettings;

internal class Program
{
    private static string? s_aiagent_id = null; //"asst_8tFjVkAnFiqSwukxKypuTdwg"

    private static async Task Main(string[] args)
    {
        #region Environment Configuration
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var ai_settings = new AISettings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {ai_settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {ai_settings.AzureOpenAI.ChatModelDeployment}");
        #endregion

        #region Semantic Kernel ChatCompletion Agent with Plugin
        Console.WriteLine("\n\n\n+++++++++++++++++ Semantic Kernel ChatCompletion Agent with Plugin +++++++++++++++++\n");

        // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
        ChatCompletionAgent? sk_chatcompletion_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "sk_chatcompletion_agent"
            ) as ChatCompletionAgent;

        // chat with the agent
        await ChatWithAgentAsync(sk_chatcompletion_agent, delete_agent_after_chat: false);
        #endregion

        #region Semantic Kernel Assistant Agent with CodeInterpreter and FileClient
        Console.WriteLine("\n\n\n+++++++++++++++++ Semantic Kernel Assistant Agent +++++++++++++++++\n");

        System.Collections.Generic.List<string> files_to_search_in = [];
        files_to_search_in.AddRange("./data/PopulationByAdmin.csv", "./data/PopulationByCountry.csv");

        // library Microsoft.SemanticKernel.Agents.OpenAI for OpenAIAssistantAgent
        OpenAIAssistantAgent? sk_assistant_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_assistant_agent",
            agent_name: "sk_assistant_agent",
            instructions: "You are a clever assistant",
            files_to_search_in: files_to_search_in,
            enableCodeInterpreter: true
            ) as OpenAIAssistantAgent;


        // chat with the agent
        await ChatWithAgentAsync(sk_assistant_agent, ai_settings: ai_settings, delete_agent_after_chat: false);
        #endregion

        #region Semantic Kernel AI Foundry Agent with Bing Grounding Tool
        Console.WriteLine("\n\n\n+++++++++++++++++ Semantic Kernel AI Foundry Agent +++++++++++++++++\n");
        Console.Write("\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        s_aiagent_id = Console.ReadLine();

        // Microsoft.SemanticKernel.Agents.AzureAI    
        AzureAIAgent? sk_aifoundry_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            bing_connection_name: ai_settings.GetVariable("BING_CONNECTION_NAME"),
            agent_type: "sk_aifoundry_agent",
            agent_name: "AnimalPicker" // this must match the name of the agent in the agents folder
            ) as AzureAIAgent;

        // chat with the agent
        await ChatWithAgentAsync(agent: sk_aifoundry_agent, delete_agent_after_chat: false);
        #endregion
    }


    // Single function to create any kind of agent
    private static async Task<object> GenericCreateAgentAsync(
        string agent_type, AISettings ai_settings, string? agent_name = null, string? bing_connection_name = null,
        string? instructions = null, System.Collections.Generic.List<string>? files_to_search_in = null, bool enableCodeInterpreter = false)
    {
        // There are two options to create the Azure AI Foundry Agent
        // - to create an Azure AI Foundry SDK object, we can directly create it with the Azure AI Foundry SDK
        // - to create a Semantic Kernel SDK object, two steps are needed: Azure AI Foundry SDK object + SK object that relies on the Azure SDK object

        object? agent = null;

        // Provided instructions take the precedence of the ones stored in the agent's file
        if (string.IsNullOrWhiteSpace(instructions))
        {
            instructions = await ReadAgentInstructionsAsync(agent_name: agent_name);
        }

        if (agent_type == "sk_chatcompletion_agent")
        {
            // Create the kernel builder with the pointer to Azure OpenAI
            var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
                deploymentName: ai_settings.AzureOpenAI.ChatModelDeployment,
                endpoint: ai_settings.AzureOpenAI.Endpoint,
                apiKey: ai_settings.AzureOpenAI.ApiKey
                );

            // Use the kernel builder to add enterprise components (for logging, in this case)
            // library  Microsoft.Extensions.Logging
            builder.Services.AddLogging(services => services.SetMinimumLevel(LogLevel.None));

            // Build the kernel
            Kernel kernel = builder.Build();

            // Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
            kernel.Plugins.AddFromType<LightsPlugin>("Lights");
            
            // Enable planning
            // library Microsoft.SemanticKernel + Microsoft.SemanticKernel.Connectors.AzureOpenAI
            var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings)
            // optional
            {
                { "repository", "microsoft/semantic-kernel" }
            };

            // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
            var sk_chatcompletion_agent = new ChatCompletionAgent
            {
                Name = agent_name,
                Instructions = instructions,
                Kernel = kernel,
                Arguments = kernelArguments ?? new KernelArguments() // Provide a default value if kernelArguments is null
            };
            agent = sk_chatcompletion_agent;
        }
        else if (agent_type == "sk_assistant_agent")
        {
            // Instantiation of the Client for Azure OpenAI 
            // library Microsoft.SemanticKernel.Agents.OpenAI for OpenAIAssistantAgent
            // library Azure.AI.OpenAI for AzureOpenAIClient
            AzureOpenAIClient openaiClient = OpenAIAssistantAgent.CreateAzureOpenAIClient(
                new AzureCliCredential(), new Uri(ai_settings.AzureOpenAI.Endpoint));

            // library OpenAI.Files for OpenAIFileClient and OpenAIFile
            OpenAIFileClient fileClient = openaiClient.GetOpenAIFileClient();
            foreach (string file_path in files_to_search_in ?? new System.Collections.Generic.List<string>())
            {
                await fileClient.UploadFileAsync(file_path, FileUploadPurpose.Assistants);
            }

            // Using the Azure OpenAI Client, now extract another Client for OpenAI Assistant Agent
            AssistantClient assistantClient = openaiClient.GetAssistantClient();
            
            var assistantDefinition = await assistantClient.CreateAssistantAsync(
                modelId: ai_settings.AzureOpenAI.ChatModelDeployment,
                name: agent_name,
                instructions: instructions,
                enableCodeInterpreter: enableCodeInterpreter
            );

            // using the definition of a specific (new or existing) OpenAI Assistant, now we may directly instantiate an OpenAIAssistantAgent 
            var sk_assistant_agent = new OpenAIAssistantAgent(definition: assistantDefinition, client: assistantClient);

            agent = sk_assistant_agent;
        }
        else if (agent_type == "sk_aifoundry_agent")
        {
            // Semantic Kernel client for the AI Foundry PROJECT - library Azure.AI.Projects for AIProjectClient
            AIProjectClient sk_project_client = AzureAIAgent.CreateAzureAIClient(
                connectionString: ai_settings.GetVariable("PROJECT_CONNECTION_STRING"),
                credential: new AzureCliCredential());

            // Semantic Kernel AGENTS client from library Azure.AI.Projects
            AgentsClient sk_agents_client = sk_project_client.GetAgentsClient();

            if (string.IsNullOrWhiteSpace(s_aiagent_id)) // if it's null, we create the agent
            {
                // Azure.AI.Projects library for BingGroundingToolDefinition and Agent
                List<BingGroundingToolDefinition> tools = await CreateBingToolsAsync(
                    sk_project_client: sk_project_client, bing_connection_name: bing_connection_name);

                // Semantic Kernel AGENT definition
                // library Azure.AI.Projects to create an Agent
                Azure.AI.Projects.Agent sk_agent_definition = await sk_agents_client.CreateAgentAsync(
                    model: ai_settings.AzureOpenAI.ChatModelDeployment,
                    name: agent_name,
                    description: agent_name,
                    instructions: instructions,
                    tools: tools
                );

                // Create Semantic Kernel AGENT, based on the agent definition (called "model" in this call)
                // library is Microsoft.SemanticKernel.Agents.AzureAI for AzureAIAgent
                var sk_ai_agent = new AzureAIAgent(
                    model:sk_agent_definition, client:sk_agents_client);

                agent = sk_ai_agent;
            }

            else // load an existing agent whose id = aiagent_id
            {
                Azure.AI.Projects.Agent sk_agent_definition = (await sk_agents_client.GetAgentAsync(assistantId: s_aiagent_id)).Value;

                // Create Semantic Kernel AGENT, based on the agent definition (called "model" in this call)
                // library is Microsoft.SemanticKernel.Agents.AzureAI for AzureAIAgent
                var sk_ai_agent = new AzureAIAgent(
                    model:sk_agent_definition, client:sk_agents_client);

                agent = sk_ai_agent;
            }
        }

        return agent;
    }

    // Single function to chat with any kind of agent
    private static async Task ChatWithAgentAsync(object agent, AISettings? ai_settings = null, bool delete_agent_after_chat = true)
    {
        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        try 
        {
            // define (without instantiating) the thread object for each agent type
            Microsoft.SemanticKernel.Agents.ChatHistoryAgentThread? sk_chatcompletionagent_thread = null;            
            Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgentThread? sk_assistant_thread = null;
            Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgentThread? sk_aiagent_thread = null;

            do
            {
                Console.WriteLine($"\nPlease ask a question to the {QuestionHinter(agent)} > "); // needed for keeping the assistant thread alive

                // Collect user input
                user_input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    exit_chat = true;
                    break;
                }

                // is it an ChatCompletionAgent agent (e.g. Microsoft.SemanticKernel.Agents.ChatCompletionAgent)?
                if (agent is Microsoft.SemanticKernel.Agents.ChatCompletionAgent sk_chatcompletion_agent)
                {
                    if (sk_chatcompletionagent_thread == null || sk_chatcompletionagent_thread.IsDeleted) // short-circuiting ;-)
                    {
                        sk_chatcompletionagent_thread = new ChatHistoryAgentThread();
                    }                    
                     
                    try
                    {
                        bool isCode = false;

                        // Microsoft.SemanticKernel.ChatMessageContent library for ChatMessageContent
                        // Microsoft.SemanticKernel.ChatCompletion library for AuthorRole
                        ChatMessageContent message = new(AuthorRole.User, user_input);

                        // here we show both streaming and non-streaming versions
                        //await foreach (ChatMessageContent response in sk_chatcompletion_agent.InvokeAsync(message: message, thread: sk_chatcompletionagent_thread))
                        await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(message: message, thread: sk_chatcompletionagent_thread))
                        {
                            // Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent.CodeInterpreterMetadataKey
                            if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                            {
                                Console.WriteLine();
                                isCode = !isCode;
                            }
                            // Display response.
                            Console.Write(response.Content);
                        }
                    }

                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                        exit_chat = true;
                    }
                    
                    finally
                    {
                        Console.Write($"\nThere are some messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive.");
                        var clear_history = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(clear_history))
                        {
                            if (clear_history.ToUpper().Trim()[0] == 'Y')
                            {
                                Console.WriteLine($"\nDeleting thread {sk_chatcompletionagent_thread.Id}... You may start a new conversation now.");

                                await sk_chatcompletionagent_thread.DeleteAsync();
                                sk_chatcompletion_agent.Kernel.Plugins.Clear(); // because the plugins "live" in the agent, not in the thread
                                sk_chatcompletion_agent.Kernel.Plugins.AddFromType<LightsPlugin>("Lights");
                            }
                        }
                    }
                }
                // is it an OpenAIAssistantAgent agent (e.g. Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent)?
                else if (agent is Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent sk_assistant_agent)
                {                     
                    if (sk_assistant_thread == null || sk_assistant_thread.IsDeleted) // short-circuiting ;-)
                    {
                        sk_assistant_thread = new(client: sk_assistant_agent.Client);
                    }                    

                    System.Collections.Generic.List<string> files_to_download = [];
                    try
                    {
                        bool isCode = false;

                        // Microsoft.SemanticKernel.ChatMessageContent library for ChatMessageContent
                        // Microsoft.SemanticKernel.ChatCompletion library for AuthorRole
                        ChatMessageContent message = new(AuthorRole.User, user_input);
                        // Microsoft.SemanticKernel.Agents.AgentThread agentThread 

                        // here we show both streaming and non-streaming versions
                        // await foreach (ChatMessageContent response in sk_ai_agent.InvokeAsync(message: message, thread: sk_assistant_thread))
                        await foreach (StreamingChatMessageContent response in sk_assistant_agent.InvokeStreamingAsync(message: message, thread: sk_assistant_thread))
                        {
                            // Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent.CodeInterpreterMetadataKey
                            if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                            {
                                Console.WriteLine();
                                isCode = !isCode;
                            }
                            // Display response.
                            Console.Write(response.Content);

                            // Capture file IDs for downloading
                            files_to_download.AddRange(response.Items.OfType<StreamingFileReferenceContent>().Select(item => item.FileId));
                        }
                    }

                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                        exit_chat = true;
                    }

                    // this code is always guaranteed to execute, regardless of whether an exception is thrown
                    finally
                    {
                        files_to_download = RemoveDuplicates(files_to_download);

                        // WE NEED TO RETRIEVE THE AzureOpenAIClient object, and use it to retrieve the OpenAIFileClient

                        // library Microsoft.SemanticKernel.Agents.OpenAI for OpenAIAssistantAgent
                        // library Azure.AI.OpenAI for AzureOpenAIClient
                        AzureOpenAIClient openaiClient = OpenAIAssistantAgent.CreateAzureOpenAIClient(
                            new AzureCliCredential(), new Uri(ai_settings.AzureOpenAI.Endpoint));

                        OpenAIFileClient file_client = openaiClient.GetOpenAIFileClient();

                        // Download any files referenced in the response
                        await DownloadResponseAsync(file_client: file_client, files_to_download: files_to_download);
                        files_to_download.Clear();


                        // collect the thread messages: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming?pivots=programming-language-csharp
                        // just for testing
                        ChatMessageContent[] messages = await sk_assistant_thread.GetMessagesAsync().ToArrayAsync();

                        int messages_count=0;
                        await foreach (var message in sk_assistant_thread.GetMessagesAsync())
                        {
                            messages_count++;
                        }

                        // We need a carriage return after a simple Write operation executed with the streaming method
                        Console.Write($"\nThere are {messages_count} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive.");
                        var clear_history = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(clear_history))
                        {
                            if (clear_history.ToUpper().Trim()[0] == 'Y')
                            {
                                Console.WriteLine($"Deleting thread {sk_assistant_thread.Id} together with all its messages...");
                                await sk_assistant_thread.DeleteAsync();
                            }
                        }
                    }
                }
                // is it an AI Foundry agent (e.g. Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent)?
                else if (agent is Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent sk_ai_agent)
                // interacting with the agent: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp#interacting-with-an-azureaiagent
                // Interaction with the AzureAIAgent is straightforward. The agent maintains the conversation history automatically using a thread.
                // The specifics of the Azure AI Agent thread is abstracted away via the AzureAIAgentThread class, which is an implementation of AgentThread.
                {
                    if (sk_aiagent_thread == null || sk_aiagent_thread.IsDeleted) // short-circuiting ;-)
                    {
                        sk_aiagent_thread = new(client: sk_ai_agent.Client);
                    }

                    try
                    {
                        bool isCode = false;
                        // Microsoft.SemanticKernel.ChatMessageContent library for ChatMessageContent
                        // Microsoft.SemanticKernel.ChatCompletion library for AuthorRole
                        ChatMessageContent message = new(AuthorRole.User, user_input);

                        // here we show both streaming and non-streaming versions
                        // await foreach (ChatMessageContent response in sk_ai_agent.InvokeAsync(message: message, thread: sk_ai_agent_thread))
                        await foreach (StreamingChatMessageContent response in sk_ai_agent.InvokeStreamingAsync(message: message, thread: sk_aiagent_thread))
                        {
                            // Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent.CodeInterpreterMetadataKey
                            if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                            {
                                Console.WriteLine();
                                isCode = !isCode;
                            }
                            // Display response.
                            Console.Write(response.Content);
                        }
                    }

                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                        exit_chat = true;
                    }

                    finally
                    {
                        // collect the thread messages: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming?pivots=programming-language-csharp
                        ChatMessageContent[] messages = await sk_aiagent_thread.GetMessagesAsync().ToArrayAsync();

                        int messages_count=0; // count the nr of messages we have in the thread
                        await foreach (var message in sk_aiagent_thread.GetMessagesAsync())
                        {
                            messages_count++;
                        }

                        // We need a carriage return after a simple Write operation executed with the streaming method
                        Console.Write($"\nThere are {messages_count} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive > ");
                        var clear_history = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(clear_history))
                        {
                            if (clear_history.ToUpper().Trim()[0] == 'Y')
                            {
                                Console.WriteLine($"Deleting thread {sk_aiagent_thread.Id} together with all its messages...");
                                await sk_aiagent_thread.DeleteAsync();
                            }
                        }
                    }
                }

            } while (!exit_chat);
        }

        finally
        {
            if (delete_agent_after_chat && agent is Microsoft.SemanticKernel.Agents.ChatCompletionAgent sk_chatcompletion_agent)
            {
                Console.WriteLine($"ChatCompletion agent {sk_chatcompletion_agent.Name}({sk_chatcompletion_agent.Id}) is automatically destroyed");                
            }
            else if (delete_agent_after_chat && agent is Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent sk_assistant_agent)
            {
                Console.WriteLine($"Deleting assistant {sk_assistant_agent.Name} ({sk_assistant_agent.Id})...");
                await sk_assistant_agent.Client.DeleteAssistantAsync(assistantId: sk_assistant_agent.Id);
            }            
            else if (delete_agent_after_chat && agent is Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent sk_ai_agent)
            {
                Console.WriteLine($"Deleting agent {sk_ai_agent.Name}({sk_ai_agent.Id})...");
                await sk_ai_agent.Client.DeleteAgentAsync(agentId: sk_ai_agent.Id);
            }            
        }
    }

    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agent_name)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agent_name}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }

    // Helper function to suggest a question hint based on the agent type
    private static string QuestionHinter(object agent)
    {
        string hint="";
        if (agent is Microsoft.SemanticKernel.Agents.ChatCompletionAgent sk_chatcompletion_agent)
        {
            hint = $"agent <{sk_chatcompletion_agent.Name}> of type <ChatCompletionAgent>, e.g. 'Toggle the porch light and give me the status of all the lights'";
        }        
        else if (agent is Microsoft.SemanticKernel.Agents.OpenAI.OpenAIAssistantAgent sk_assistant_agent)
        {
            hint = $"agent <{sk_assistant_agent.Name}> of type <OpenAIAssistantAgent>, e.g. 'Create a 3D pie chart with the top 6 countries by population in Europe, showing absolute numbers'";
        }
        else if (agent is Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent sk_ai_agent)
        {
            hint = $"agent <{sk_ai_agent.Name}> of type <AzureAIAgent>, e.g. 'What is the biggest reptile?'";
        }
        else
        {
            hint = "agent <unknown>, e.g. 'How can I make a nice pizza?'";
        }

        return hint;
    }

    // Helper function to create a Bing tool
    private static async Task<List<Azure.AI.Projects.BingGroundingToolDefinition>> CreateBingToolsAsync(
        Azure.AI.Projects.AIProjectClient sk_project_client, string bing_connection_name)
    {
        if (string.IsNullOrWhiteSpace(bing_connection_name))
        {
            return null;
        }

        // Azure.AI.Projects library for both ConnectionsClient and ConnectionResponse
        ConnectionsClient cxnClient = sk_project_client.GetConnectionsClient();
        ConnectionResponse bingConnection = (await cxnClient.GetConnectionAsync(bing_connection_name)).Value;

        // Azure.AI.Projects library for both ToolConnectionList and BingGroundingToolDefinition
        var connectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(bingConnection.Id) }
        };
        var bingGroundingTool = new BingGroundingToolDefinition(connectionList);
        var tools = new List<BingGroundingToolDefinition>{bingGroundingTool};
        return tools;
    }

    // Helper function to remove duplicates from a string list, when it's built by a streaming function
    private static System.Collections.Generic.List<string> RemoveDuplicates(System.Collections.Generic.List<string> fileIds)
    {
        // Using HashSet to remove duplicates
        var uniqueFileIds = new HashSet<string>(fileIds);

        // Converting HashSet back to List
        return uniqueFileIds.ToList();
    }

    // Helper function to coordingate the download of all files
    private static async Task DownloadResponseAsync(OpenAIFileClient file_client, System.Collections.Generic.List<string> files_to_download)
    {
        if (files_to_download.Count > 0)
        {
            Console.WriteLine();
            foreach (string fileId in files_to_download)
            {
                await DownloadSingleFileContentAsync(file_client, fileId, launchViewer: true);
            }
        }
    }

    // Helper function to download a SINGLE file from OpenAI
    private static async Task DownloadSingleFileContentAsync(OpenAIFileClient client, string fileId, bool launchViewer = false)
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
}