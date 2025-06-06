// Copyright (c) Microsoft. All rights reserved.

// Last update: June 6th, 2025

// Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
// AzureAIAgent Foundry GA Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
// Microsoft.SemanticKernel.Agents Namespace (prerelease): https://learn.microsoft.com/it-it/dotnet/api/microsoft.semantickernel.agents?view=semantic-kernel-dotnet

/*
Features included:
- Azure.AI.Agents.Persistent library
- Microsoft.SemanticKernel.Agents.AzureAI library
- Bing Grounding tool
- Multiple types of agents

*/

// Create Agent with Bing Grounding: https://www.nuget.org/packages/Azure.AI.Agents.Persistent/1.0.0#create-agent-with-bing-grounding

// dotnet new console -n "SK 08 - ChatCompletion + Assistant + AI Foundry agents (no group)_NEW" --framework net9.0

// dotnet add package Azure.AI.Agents.Persistent --> <PackageReference Include="Azure.AI.Agents.Persistent" Version="1.0.0" />
using Azure.AI.Agents.Persistent;

// dotnet add package Microsoft.SemanticKernel.Agents.Core --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.55.0" />
using Microsoft.SemanticKernel.Agents; // needed for ChatCompletion

// dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.55.0-preview" />
using Microsoft.SemanticKernel.Agents.AzureAI;


// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;
using Azure;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.Extensions.Logging;

namespace LLMSettings;


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
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {ai_settings.AzureOpenAI.ChatModelDeployment}" +
        $"PROJECT_ENDPOINT: {ai_settings.AzureOpenAI.ProjectEndpoint}");
        #endregion

        #region Semantic Kernel ChatCompletion Agent with Plugin
        Console.WriteLine("\n\n\n+++++++++++++++++ Semantic Kernel ChatCompletion Agent with Plugin +++++++++++++++++\n");

        // library Microsoft.SemanticKernel.Agents for ChatCompletionAgent
        ChatCompletionAgent? sk_chatcompletion_agent = await GenericCreateAgentAsync(
            ai_settings: ai_settings,
            agent_type: "sk_chatcompletion_agent",
            agent_name: "sk_chatcompletion_agent",
            instructions: "You are a clever chat completion agent"
            ) as ChatCompletionAgent;

        // chat with the agent
        // await GenericChatWithAgentAsync(sk_chatcompletion_agent, delete_agent_after_chat: false);
        #endregion

        #region Semantic Kernel Assistant Agent with CodeInterpreter and FileClient
        #endregion

        #region Semantic Kernel AI Foundry Agent
        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        s_aiagent_id = Console.ReadLine();

        PersistentAgentsClient aiproject_client = AzureAIAgent.CreateAgentsClient(ai_settings.GetVariable("PROJECT_ENDPOINT"), new AzureCliCredential());

        // Microsoft.SemanticKernel.Agents.AzureAI    
        PersistentAgent? sk_ai_agent = await GenericCreateAgentAsync(
            agent_type: "azure_aifoundry_agent",
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
        AISettings? ai_settings = null, string? instructions = null, string? aiagent_id = null)
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
                apiKey: ai_settings.AzureOpenAI.ApiKey);

            // Use the kernel builder to add enterprise components (for logging, in this case)
            // builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.None)); // fix this

            // Build the kernel
            Kernel kernel = builder.Build();

            // Enable planning
            // if "pure" OpenAI, please use OpenAIPromptExecutionSettings
            // in Azure OpenAI, we have     AzureChatPromptExecutionSettings
            var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
            var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings)
            // optional
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
        }
        else if (agent_type == "azure_aifoundry_agent")
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

        do
        {
            Console.Write("\nUser, e.g. 'what is the biggest insect?' > ");

            // Collect user input
            user_input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                exit_chat = true;
                break;
            }

            // Azure.AI.Agents.Persistent.PersistentAgent
            if (agent is PersistentAgent sk_ai_agent)
            {
                PersistentAgentThread thread = await aiproject_client.Threads.CreateThreadAsync();
                PersistentThreadMessage message = await aiproject_client.Messages.CreateMessageAsync(
                    thread.Id,
                    MessageRole.User, user_input);

                ThreadRun run = await aiproject_client.Runs.CreateRunAsync(
                    thread.Id,
                    sk_ai_agent.Id,
                    additionalInstructions: "Please address the user as Jane Doe. The user has a premium account.");

                // Once the run has started, it should then be polled until it reaches a terminal status:
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    run = await aiproject_client.Runs.GetRunAsync(thread.Id, run.Id);
                }
                while (run.Status == RunStatus.Queued
                    || run.Status == RunStatus.InProgress);

                /* Assert.AreEqual(
                    RunStatus.Completed,
                    run.Status,
                    run.LastError?.Message);*/

                // Retrieve the messages from the run: assuming the run successfully completed, listing messages from the thread that was run will now reflect new information added by the agent


                AsyncPageable<PersistentThreadMessage> messages =
                    aiproject_client.Messages.GetMessagesAsync(
                        threadId: thread.Id, order: ListSortOrder.Ascending);

                await foreach (PersistentThreadMessage threadMessage in messages)
                {
                    Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
                    foreach (MessageContent contentItem in threadMessage.ContentItems)
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

            }
        } while (!exit_chat);
    }


    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agent_name)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agent_name}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }

}