// Copyright (c) Microsoft. All rights reserved.

// Last update: June 6th, 2025

// Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
// AzureAIAgent Foundry GA Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
// Microsoft.SemanticKernel.Agents Namespace (prerelease): https://learn.microsoft.com/it-it/dotnet/api/microsoft.semantickernel.agents?view=semantic-kernel-dotnet
// Create Agent with Bing Grounding: https://www.nuget.org/packages/Azure.AI.Agents.Persistent/1.0.0#create-agent-with-bing-grounding

// dotnet new console -n "SK 07 - AI Foundry with persistent library_NEW" 

// dotnet add package Azure.AI.Agents.Persistent --> <PackageReference Include="Azure.AI.Agents.Persistent" Version="1.0.0" />
using Azure.AI.Agents.Persistent;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.55.0" />
// using Microsoft.SemanticKernel;

// dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.55.0-preview" />
using Microsoft.SemanticKernel.Agents.AzureAI;


// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;
using Azure;

namespace LLMSettings;


internal class Program
{
    private static string? s_aiagent_id = null;
    private static async Task Main(string[] args)
    {
        #region Environment Configuration
        Console.WriteLine("Application starts");
        // Load configuration from environment variables or user secrets.
        var aiSettings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}" +
        $"PROJECT_ENDPOINT: {aiSettings.AzureOpenAI.ProjectEndpoint}");
        #endregion


        #region Semantic Kernel AI Foundry Agent
        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        s_aiagent_id = Console.ReadLine();

        PersistentAgentsClient aiproject_client = AzureAIAgent.CreateAgentsClient(aiSettings.GetVariable("PROJECT_ENDPOINT"), new AzureCliCredential());

        // Microsoft.SemanticKernel.Agents.AzureAI    
        PersistentAgent? sk_ai_agent = await GenericCreateAgentAsync(
            agent_type: "azure_aifoundry_agent",
            agent_name: "AnimalPicker",
            aiagent_id: s_aiagent_id,
            // aiproject_endpoint: aiSettings.GetVariable("PROJECT_ENDPOINT"),
            aiproject_client: aiproject_client,
            deployment_name: aiSettings.GetVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            bing_connection_id: aiSettings.GetVariable("BING_CONNECTION_ID")
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
        string agent_type, string? agent_name = null, PersistentAgentsClient? aiproject_client = null, string? aiproject_endpoint = null, string? deployment_name = null,
        string? bing_connection_id = null, string? aiagent_id = null)
    {
        // There are two options to create the Azure AI Foundry Agent
        // - to create an Azure AI Foundry SDK object, we can directly create it with the Azure AI Foundry SDK
        // - to create a Semantic Kernel SDK object, two steps are needed: Azure AI Foundry SDK object + SK object that relies on the Azure SDK object

        object? agent = null;

        if (agent_type == "azure_aifoundry_agent")
        {

            BingGroundingToolDefinition bingGroundingTool = new(
                bingGrounding: new BingGroundingSearchToolParameters(
                    [new BingGroundingSearchConfiguration(connectionId: bing_connection_id)]
                )
            );


            PersistentAgent sk_ai_agent;
            if (string.IsNullOrWhiteSpace(aiagent_id))
            {
                sk_ai_agent = await aiproject_client.Administration.CreateAgentAsync(
                model: deployment_name,
                    name: agent_name,
                    description: agent_name,
                    instructions: await ReadAgentInstructionsAsync(agentName: agent_name),
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
    private static async Task<string> ReadAgentInstructionsAsync(string agentName)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }

}