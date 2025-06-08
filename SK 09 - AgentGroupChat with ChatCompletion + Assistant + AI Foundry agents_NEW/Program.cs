// Copyright (c) Microsoft. All rights reserved.

// Last update: June 6th, 2025

// Created with: dotnet new console -n "SK 09 - AgentGroupChat with ChatCompletion + Assistant + AI Foundry agents_NEW" --framework net9.0

/*
Supporting documentation:
- Semantic Kernel Agents are now Generally Available: https://devblogs.microsoft.com/semantic-kernel/semantic-kernel-agents-are-now-generally-available/
- Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
- Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
- AzureAIAgent Foundry GA Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp
- Semantic Kernel: Multi-agent Orchestration: https://devblogs.microsoft.com/semantic-kernel/semantic-kernel-multi-agent-orchestration/
- AgentGroupChat Orchestration Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/group-chat-orchestration-migration-guide?pivots=programming-language-csharp
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
using Microsoft.SemanticKernel.Agents.OpenAI;
using OpenAI.Assistants;
using Azure.AI.OpenAI;
using OpenAI.Files;
// using OpenAI.VectorStores;

// dotnet add package Microsoft.SemanticKernel.Agents.Orchestration --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Orchestration" Version="1.55.0-preview" />
using Microsoft.SemanticKernel.Agents.Orchestration.Sequential; // needed for SequentialOrchestration
// using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;


// JUST FOR ASSISTANT API'S
// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --><PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.55.0-preview" />


// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;
using Microsoft.SemanticKernel.ChatCompletion;

using AIPlugins;
using Azure.AI.Projects;
using Microsoft.SemanticKernel.Agents.AzureAI;

namespace LLMSettings;

#endregion

internal class Program
{
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

        #region CreatureQuestioner (SK ChatCompletion Agent)
        Console.WriteLine("\n\n\n+++++++++++++++++ CreatureQuestioner (SK ChatCompletion Agent) +++++++++++++++++\n");

        // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
        ChatCompletionAgent? creaturequestioner_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "CreatureQuestioner"
            ) as ChatCompletionAgent;

        // chat with the agent
        var creaturequestioner_response = await GenericChatWithAgentAsync(
            agent: creaturequestioner_agent,
            predefined_question: "doesn't matter");
        #endregion

        #region AnimalPicker (SK AI Foundry Agent with Bing Grounding Tool)
        Console.WriteLine("\n\n\n+++++++++++++++++ AnimalPicker (AI Foundry Agent with Bing Grounding Tool) +++++++++++++++++\n");
        // Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        // s_aiagent_id = Console.ReadLine();

        var aiproject_client = new AIProjectClient(new Uri(ai_settings.GetVariable("PROJECT_ENDPOINT")), new AzureCliCredential());
        // we could create the project agent without the project client, but we need it for the deletion
        PersistentAgentsClient aiagents_client = aiproject_client.GetPersistentAgentsClient();

        // Microsoft.SemanticKernel.Agents.AzureAI    
        AzureAIAgent? animalpicker_agent = await GenericCreateAgentAsync(
            agent_type: "sk_azure_aifoundry_agent",
            agent_name: "AnimalPicker",
            aiagents_client: aiagents_client,
            ai_settings: ai_settings
            ) as AzureAIAgent;


        // chat with the agent
        var animalpicker_response = await GenericChatWithAgentAsync(
            agent: animalpicker_agent,
            predefined_question: creaturequestioner_response);
        #endregion

        #region AnimalJoker (SK ChatCompletion Agent)
        Console.WriteLine("\n\n\n+++++++++++++++++ AnimalJoker (SK ChatCompletion Agent) +++++++++++++++++\n");

        // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
        ChatCompletionAgent? animaljoker_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "AnimalJoker"
            ) as ChatCompletionAgent;

        // chat with the agent
        var animaljoker_response = await GenericChatWithAgentAsync(agent: animaljoker_agent, predefined_question: animalpicker_response);
        #endregion

        #region Statistician (SK Assistant Agent with CodeInterpreter)
        Console.WriteLine("\n\n\n+++++++++++++++++ Statistician (SDK Assistant Agent with CodeInterpreter) +++++++++++++++++\n");

        // In this case, I do NOT use CodeInterpreter or FileClient tools. If you need them, please consider example #8

        // library Microsoft.SemanticKernel.Agents.OpenAI for OpenAIAssistantAgent
        OpenAIAssistantAgent? statistician_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_assistantapi_agent",
            agent_name: "Statistician"
            ) as OpenAIAssistantAgent;

        // chat with the agent
        var statistician_response = await GenericChatWithAgentAsync(
            agent: statistician_agent,
            predefined_question: animaljoker_response);
        #endregion

        #region Reviewer (SK ChatCompletion Agent)
        Console.WriteLine("\n\n\n+++++++++++++++++ Reviewer (SK ChatCompletion Agent) +++++++++++++++++\n");

        // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
        ChatCompletionAgent? reviewer_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "Reviewer"
            ) as ChatCompletionAgent;

        // chat with the agent
        var reviewer_response = await GenericChatWithAgentAsync(
            agent: reviewer_agent,
            predefined_question: statistician_response);
        #endregion


        // SequentialOrchestration orchestration = new(animalpicker_agent, animaljoker_agent);

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

    // Single Chat function for all kinds of agents
    private static async Task<object> GenericCreateAgentAsync(
        string agent_type, string? agent_name = null, PersistentAgentsClient? aiagents_client = null,
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

            /*

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

            */

            // Get the ASSISTANT API CLIENT
            AssistantClient assistant_client = azure_openai_client.GetAssistantClient();

            // Create an ASSISTANT (or ASSISTANT DEFINITION)
            Assistant assistant = await assistant_client.CreateAssistantAsync(
                modelId: ai_settings.AzureOpenAI.ChatModelDeployment, // deployment name
                name: agent_name,
                instructions: instructions,
                enableCodeInterpreter: true
                //codeInterpreterFileIds: s_id_files_to_work_with,
                //enableFileSearch: true,
                //vectorStoreId: storeId
                ); // includes s_opeaifiles_to_search_in
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

            PersistentAgent sk_ai_agent_definition;
            if (string.IsNullOrWhiteSpace(aiagent_id))
            {
                sk_ai_agent_definition = await aiagents_client.Administration.CreateAgentAsync(
                    model: ai_settings.AzureOpenAI.ChatModelDeployment,
                    name: agent_name,
                    description: agent_name,
                    instructions: instructions,
                    tools: [bingGroundingTool]
                );

            }
            else
            {
                sk_ai_agent_definition = await aiagents_client.Administration.GetAgentAsync(aiagent_id);
            }

            agent = new AzureAIAgent(sk_ai_agent_definition, aiagents_client);
        }

        return agent;
    }


    // Single Chat function for all kinds of agents
    private static async Task<string> GenericChatWithAgentAsync(object agent, string? predefined_question = null)
    {
        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;
        string? agent_response = "";

        if (agent is ChatCompletionAgent sk_chatcompletion_agent)
        {
            Console.Write("\nWelcome to the ChatCompletionAgent! Ask me anything, or type 'EXIT' to quit.\n");

            // Create a history store the conversation, however use ChatHistoryAgentThread instead of ChatHistory, which is deprecated
            var sk_chatcompletionagent_thread = new ChatHistoryAgentThread();

            do
            {
                if (string.IsNullOrWhiteSpace(predefined_question))
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
                }
                else
                {
                    user_input = predefined_question;
                }

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
                    agent_response += response.Content;
                }

                if (string.IsNullOrWhiteSpace(predefined_question))
                {
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
                }
                else
                {
                    exit_chat = true;
                }

            } while (!exit_chat);
        }


        else if (agent is OpenAIAssistantAgent sk_assistantapi_agent)
        {
            Console.Write("\nWelcome to the OpenAI Assistant agent! Ask me anything, or type 'EXIT' to quit.\n");

            // Here we just define (without creating) the AgentThread variable
            // An AgentThread will be started and returned as part of the response
            AgentThread? agent_thread = null;

            do
            {
                if (string.IsNullOrWhiteSpace(predefined_question))
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
                }
                else
                {
                    user_input = predefined_question;
                }

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
                    agent_response += response.Message.Content;
                    agent_thread = response.Thread;
                }

                if (string.IsNullOrWhiteSpace(predefined_question))
                {

                    Console.Write($"\n\nEnter 'Y' if you want to clear the history, or anything else to keep the thread and its messages alive > ");
                    var clear_history = Console.ReadLine();
                    // Delete the thread if no longer needed
                    if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
                    {
                        // await agent_thread.DeleteAsync();
                        agent_thread = null;
                    }
                }
                else
                {
                    exit_chat = true;
                }

            } while (!exit_chat);
        }


        else if (agent is AzureAIAgent sk_aifoundry_agent)
        {
            Console.Write("\nWelcome to the AI Foundry Agent (PersistentAgent object)! Ask me anything, or type 'EXIT' to quit.\n");
            do
            {
                if (string.IsNullOrWhiteSpace(predefined_question))
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
                }
                else
                {
                    user_input = predefined_question;
                }

                if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    exit_chat = true;
                    break;
                }

                Console.WriteLine("Chatting with the AI Foundry agent...");

                AzureAIAgentThread agentThread = new(sk_aifoundry_agent.Client);
                ChatMessageContent message = new(AuthorRole.User, user_input);

                // await foreach (ChatMessageContent response in sk_aifoundry_agent.InvokeAsync(message2, agentThread)) // non-streaming version
                await foreach (StreamingChatMessageContent response in sk_aifoundry_agent.InvokeStreamingAsync(message, agentThread)) // streaming version
                {
                    Console.Write(response.Content);
                    agent_response += response.Content;
                }


                if (string.IsNullOrWhiteSpace(predefined_question))
                {
                    Console.Write($"\n\nEnter 'Y' if you want to clear the history, or anything else to keep the thread and its messages alive > ");
                    var clear_history = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
                    {
                        await agentThread.DeleteAsync();
                    }
                }
                else
                {
                    await agentThread.DeleteAsync();
                    exit_chat = true;
                }

            } while (!exit_chat);

        }

        return agent_response;
    }

    /*
        private static Microsoft.SemanticKernel.Agents.AgentGroupChat GroupChatCreateAgentAsync(PersistentAgent animalpicker_agent, ChatCompletionAgent? animaljoker_agent,
            OpenAIAssistantAgent? statistician_agent, ChatCompletionAgent? reviewer_agent, ChatCompletionAgent? creaturequestioner_agent, AISettings ai_settings)
        {
            // AGENT GROUP CHAT PREPARATION

            // "Termination" kernel function that responds "yes" if the last message is satisfactory
            const string TerminationToken = "YES";
            KernelFunction terminationFunction =
                AgentGroupChat.CreatePromptFunctionForStrategy(
                    $$$"""
                    Examine the **RESPONSE** and determine whether the content has been deemed satisfactory.
                    If content is satisfactory, respond with a single word without explanation: {{{TerminationToken}}}.
                    If specific suggestions are being provided without an explicit satisfaction check, it is not satisfactory.
                    If it's made of a single word that doesn't mean being satisfactory, it's not satisfactory.

                    **RESPONSE**:
                    {{$lastmessage}}
                    """,

                    safeParameterNames: "lastmessage");

            // "Selection" kernel function that receives the last message and responds the name of the next participant
            KernelFunction selectionFunction =
                AgentGroupChat.CreatePromptFunctionForStrategy(
                    $$$"""
                    Examine the provided **RESPONSE** and generate **EXCLUSIVELY A SINGLE WORD** with the name of the next participant.

                    Choose only from these participants:
                    - {{{creaturequestioner_agent.Name}}}
                    - {{{animalpicker_agent.Name}}}
                    - {{{animaljoker_agent.Name}}}
                    - {{{statistician_agent.Name}}}
                    - {{{reviewer_agent.Name}}}

                    Always follow these rules when choosing the next participant:
                    - If RESPONSE is user input, it is {{{animalpicker_agent.Name}}}'s turn.
                    - If RESPONSE is by {{{animalpicker_agent.Name}}}, it is {{{animaljoker_agent.Name}}}'s turn.
                    - If RESPONSE is by {{{animaljoker_agent.Name}}}, it is {{{statistician_agent.Name}}}'s turn.
                    - If RESPONSE is by {{{statistician_agent.Name}}}, it is {{{reviewer_agent.Name}}}'s turn.
                    - If RESPONSE is by {{{reviewer_agent.Name}}}, it is either {{{creaturequestioner_agent.Name}}}'s turn, or the conversazion is complete.
                    - If RESPONSE is by {{{creaturequestioner_agent.Name}}}, it is {{{animalpicker_agent.Name}}}'s turn.

                    RESPONSE:
                    {{$lastmessage}}
                    """,

                    safeParameterNames: "lastmessage");

            // history reducer that extracts the last message from the history
            var historyReducer = new ChatHistoryTruncationReducer(targetCount: 1);

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

            var orchestration = new Microsoft.SemanticKernel.Agents.Orchestration.GroupChat.GroupChatOrchestration(
                terminationFunction: terminationFunction,
                selectionFunction: selectionFunction,
                kernel: kernel,
                historyReducer: historyReducer

            )

            // create the Group Chat Agent
            var groupChatAgent = new AgentGroupChat(animalpicker_agent animaljoker_agent, statistician_agent, reviewer_agent, creaturequestioner_agent)
            {
                // library Microsoft.SemanticKernel.Agents.Chat for AgentGroupChatSettings
                ExecutionSettings = new AgentGroupChatSettings
                {
                    TerminationStrategy = new KernelFunctionTerminationStrategy(function: terminationFunction, kernel: kernel)
                    {
                        Agents = [reviewer_agent], // Only evaluate for editor's response
                        HistoryReducer = historyReducer, // Save tokens by only including the final response
                        HistoryVariableName = "lastmessage", // The prompt variable name for the history argument.
                        // Customer result parser to determine if the response is "yes":
                        ResultParser = (result) => result.GetValue<string>()?.Contains(TerminationToken, StringComparison.OrdinalIgnoreCase) ?? false,
                        MaximumIterations = 50, // Limit total number of turns
                    },

                    SelectionStrategy = new KernelFunctionSelectionStrategy(function: selectionFunction, kernel: kernel)
                    {
                        InitialAgent = animalpicker_agent, // Always start with the editor agent.
                        HistoryReducer = historyReducer, // Save tokens by only including the final response
                        HistoryVariableName = "lastmessage", // The prompt variable name for the history argument.

                        // Returns the entire result value as a string
                        // "nullish coalescing" (??) operator returns the left value if not null/undefined; otherwise it returns the value on the right
                        ResultParser = (result) => result.GetValue<string>() ?? reviewer_agent.Name
                    }
                }
            };

            return groupChatAgent;
        }
    */
}