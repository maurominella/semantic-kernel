// Copyright (c) Microsoft. All rights reserved.

// Last update: June 6th, 2025

// Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.agents.persistent-readme?view=azure-dotnet
// AzureAIAgent Foundry GA Migration Guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp
// Azure.AI.Agents.Persistent Namespace: https://learn.microsoft.com/en-us/dotnet/api/azure.ai.agents.persistent?view=azure-dotnet
// Microsoft.SemanticKernel.Agents Namespace (prerelease): https://learn.microsoft.com/it-it/dotnet/api/microsoft.semantickernel.agents?view=semantic-kernel-dotnet

/*
Features included:
- Azure.AI.Agents.Persistent library
- Microsoft.SemanticKernel.Agents.AzureAI library
- Bing Grounding tool

*/

// Create Agent with Bing Grounding: https://www.nuget.org/packages/Azure.AI.Agents.Persistent/1.0.0#create-agent-with-bing-grounding

// dotnet new console -n "SK 07 - AI Foundry with persistent library_NEW" 

// dotnet add package Azure.AI.Agents.Persistent --> <PackageReference Include="Azure.AI.Agents.Persistent" Version="1.1.0" />
using Azure.AI.Agents.Persistent;
using Azure;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.61.0" />
// using Microsoft.SemanticKernel;

// dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.61.0-preview" />
using Azure.AI.Projects;


// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.2" />
using Azure.Identity;

namespace LLMSettings;


internal class Program
{
    private static string? s_aiagent_id = null;
    private static async Task Main(string[] args)
    {
        Console.WriteLine("\n+++++++++++++++++ Application starts +++++++++++++++++");

        #region Environment Configuration
        Console.WriteLine("\n\n\n+++++++++++++++++ Environment Configuration +++++++++++++++++\n");
        // Load configuration from environment variables or user secrets.
        var ai_settings = new AISettings();

        var project_endpoint = ai_settings.GetVariable("AIF_BAS_PROJECT_ENDPOINT");
        var project_group_name = ai_settings.GetVariable("AIF_BAS_PROJECT_GROUP_NAME");

        var openai_endpoint = ai_settings.GetVariable("AZURE_OPENAI_ENDPOINT");
        var chat_deployment_name = ai_settings.GetVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME");
        var azure_subscription_id = ai_settings.GetVariable("AZURE_SUBSCRIPTION_ID");
        var bing_connection_name = ai_settings.GetVariable("BING_GROUNDING_CONNECTION_NAME");

        var bing_connection_id = BingConnectionId(
            project_endpoint: project_endpoint,
            subscription_id: azure_subscription_id,
            resource_group: project_group_name,
            bingconnection_name: bing_connection_name
        );

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {openai_endpoint}\n" +
            $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {chat_deployment_name}\n" +
            $"PROJECT_ENDPOINT: {project_endpoint}");
        #endregion


        #region Semantic Kernel AI Foundry Agent
        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        s_aiagent_id = Console.ReadLine();

        var aiproject_client = new AIProjectClient(new Uri(project_endpoint), new AzureCliCredential());
        // we could create the project agent without the project client, but we need it for the deletion
        PersistentAgentsClient aiagents_client = aiproject_client.GetPersistentAgentsClient();

        // Microsoft.SemanticKernel.Agents.AzureAI    
        PersistentAgent? sk_ai_agent = await GenericCreateAgentAsync(
            agent_type: "azure_aifoundry_agent",
            agent_name: "AnimalPicker",
            aiagent_id: s_aiagent_id,
            // aiproject_endpoint: project_endpoint,
            aiagents_client: aiagents_client,
            deployment_name: chat_deployment_name,
            bing_connection_id: bing_connection_id
            ) as PersistentAgent;

        try
        {
            await ChatWithAgentAsync(
                sk_ai_agent,
                aiagents_client: aiagents_client);
        }
        finally
        {
            Console.WriteLine($"\nDeleting agent {sk_ai_agent.Name}({sk_ai_agent.Id})...");
            await aiagents_client.Administration.DeleteAgentAsync(agentId: sk_ai_agent.Id);
        }
        #endregion
    }


    // Single Chat function for all kinds of agents
    private static async Task<object> GenericCreateAgentAsync(
        string agent_type, string? agent_name = null, PersistentAgentsClient? aiagents_client = null, string? deployment_name = null,
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


            PersistentAgent? sk_ai_agent;
            if (string.IsNullOrWhiteSpace(aiagent_id))
            {
                sk_ai_agent = await aiagents_client.Administration.CreateAgentAsync(
                model: deployment_name,
                    name: agent_name,
                    description: agent_name,
                    instructions: await ReadAgentInstructionsAsync(agentName: agent_name),
                    tools: [bingGroundingTool]
                );

            }
            else
            {
                sk_ai_agent = await aiagents_client.Administration.GetAgentAsync(aiagent_id);
            }

            agent = sk_ai_agent;
        }

        return agent;
    }


    // Single Chat function for all kinds of agents
    private static async Task ChatWithAgentAsync(object agent, PersistentAgentsClient? aiagents_client = null)
    {
        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        do
        {
            Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- what is the biggest insect? (e.g. grounding with Bing Search),
- what's the animal of the year for 2024? (e.g. grounding with Bing Search),
- what's the biggest mammal? (e.g. grounding with Bing Search),
- what's the biggest mammal, which does not swim? (e.g. grounding with Bing Search),

Your turn > ");

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
                PersistentAgentThread thread = await aiagents_client.Threads.CreateThreadAsync();
                PersistentThreadMessage message = await aiagents_client.Messages.CreateMessageAsync(
                    thread.Id,
                    MessageRole.User, user_input);

                ThreadRun run = await aiagents_client.Runs.CreateRunAsync(
                    thread.Id,
                    sk_ai_agent.Id,
                    additionalInstructions: "Please address the user as Jane Doe. The user has a premium account.");

                // Once the run has started, it should then be polled until it reaches a terminal status:
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    run = await aiagents_client.Runs.GetRunAsync(thread.Id, run.Id);
                }
                while (run.Status == RunStatus.Queued
                    || run.Status == RunStatus.InProgress);

                /* Assert.AreEqual(
                    RunStatus.Completed,
                    run.Status,
                    run.LastError?.Message);*/


                // Retrieve the messages from the run: assuming the run successfully completed, listing messages from the thread that was run will now reflect new information added by the agent
                AsyncPageable<PersistentThreadMessage> messages =
                    aiagents_client.Messages.GetMessagesAsync(
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

    private static string BingConnectionId(
        string project_endpoint, string subscription_id, string resource_group, string bingconnection_name)
    {
        // Extract the account name from the project_endpoint URL
        // Assumes the format: https://{account_name}.services.ai.azure.com/...
        var uri = new Uri(project_endpoint);
        string host = uri.Host; // e.g., aif2stdsvhdu2.services.ai.azure.com
        string account_name = host.Split('.')[0]; // e.g., aif2stdsvhdu2
        string project_name = uri.ToString().Split('/').Last(); // e.g., aif2stdsvhdu2

        // Construct the connection ID string
        string connection_id = $"/subscriptions/{subscription_id}/resourceGroups/{resource_group}/providers/Microsoft.CognitiveServices/accounts/{account_name}/projects/{project_name}/connections/{bingconnection_name}";

        return connection_id;
    }

}