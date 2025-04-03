﻿// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// Microsoft.SemanticKernel.Agents.AzureAI library documentation: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/azure-ai-agent?pivots=programming-language-csharp
// BING: https://learn.microsoft.com/en-us/azure/ai-services/agents/how-to/tools/bing-grounding?tabs=csharp&pivots=code-example#step-2-create-an-agent-with-the-grounding-with-bing-search-tool-enabled
// Migration guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide?pivots=programming-language-csharp
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.44.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.44.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;

using Azure.Identity;
using Microsoft.SemanticKernel.Agents.AzureAI;

namespace LLMSettings;
internal class Program
{
    private static string? aiagent_id = null; //"asst_8tFjVkAnFiqSwukxKypuTdwg"; // asst_8tFjVkAnFiqSwukxKypuTdwg

    private static async Task Main(string[] args)
    {
        #region Environment Configuration
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var aiSettings = new AISettings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}");
        #endregion

        #region Semantic Kernel AI Foundry Agent

        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        aiagent_id = Console.ReadLine();

            
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent? sk_agent = await GenericCreateAgentAsync(
            agent_type: "azure_aifoundry_agent",
            agent_name: "AnimalPicker",
            aiproject_connection_string: aiSettings.GetVariable("PROJECT_CONNECTION_STRING"),
            deployment_name: aiSettings.GetVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            bing_connection_name: aiSettings.GetVariable("BING_CONNECTION_NAME")
            ) as Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent;

/*
        // Test the AnimalPicker agent
        Console.WriteLine("\n\nTest the single Agent \"AnimalPicker\" (an AI Foundry Agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: sk_animalpicker_agent, sk_project_client: sk_project_client);
        Console.WriteLine($"Deleting the agent {sk_animalpicker_agent.Name} ({sk_animalpicker_agent.Id})...");
        await sk_project_client.Client.GetAgentsClient().DeleteAgentAsync(agentId: sk_animalpicker_agent.Id);
*/
        #endregion
    }


    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agentName)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }


    // Single Chat function for all kinds of agents
    private static async Task<object> GenericCreateAgentAsync(
        string agent_type, string? agent_name = null, string? aiproject_connection_string = null, string? deployment_name = null,
        string? bing_connection_name = null, Azure.AI.Projects.Agent? azure_aifoundry_agent = null)
    {
        // There are two options to create the Azure AI Foundry Agent
        // - to create an Azure AI Foundry SDK object, we can directly create it with the Azure AI Foundry SDK
        // - to create a Semantic Kernel SDK object, two steps are needed: Azure AI Foundry SDK object + SK object that relies on the Azure SDK object

        object? agent = null;

        if (agent_type == "azure_aifoundry_agent")
        {
            // Semantic Kernel client for the AI Foundry PROJECT
            Azure.AI.Projects.AIProjectClient sk_project_client = AzureAIAgent.CreateAzureAIClient(
                connectionString: aiproject_connection_string,
                credential: new AzureCliCredential());

            // Semantic Kernel AGENTS client
            Azure.AI.Projects.AgentsClient sk_agents_client = sk_project_client.GetAgentsClient();

            if (string.IsNullOrWhiteSpace(aiagent_id)) // if it's null, we create the agent
            {
                List<Azure.AI.Projects.BingGroundingToolDefinition> tools = await CreateBingToolsAsync(
                    sk_project_client: sk_project_client, bing_connection_name: bing_connection_name);

                // Semantic Kernel AGENT definition
                Azure.AI.Projects.Agent sk_agent_definition = await sk_agents_client.CreateAgentAsync(
                    model: deployment_name,
                    name: agent_name,
                    description: agent_name,
                    instructions: await ReadAgentInstructionsAsync(agentName: agent_name),
                    tools: tools
                );

                // Create Semantic Kernel AGENT, based on the agent definition (called "model" in this call)
                var sk_agent = new Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent(
                    model:sk_agent_definition, client:sk_agents_client);

                
                agent = sk_agent;
            }

            else // load an existing agent whose id = aiagent_id
            {

                Azure.AI.Projects.Agent sk_agent_definition = (await sk_agents_client.GetAgentAsync(assistantId: aiagent_id)).Value;

                // Create Semantic Kernel AGENT, based on the agent definition (called "model" in this call)
                var sk_agent = new Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent(
                    model:sk_agent_definition, client:sk_agents_client);

                agent = sk_agent;
            }
        }

        else if (agent_type == "sk_aifoundry_agent")
        {

            // Semantic Kernel SDK Agent. WE NEED TO "CLONE" THE AZURE AI AGENT TO CREATE THIS!!
            // Semantic Kernel AGENTS client
            // Azure.AI.Projects.AgentsClient sk_agents_client = sk_project_client.GetAgentsClient();
            agent = null;
        }

        return agent;
    }

/*
    // Single Chat function for all kinds of agents
    private static async Task ChatWithAgentAsync(
        object agent, Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider? sk_project_client = null)
    {
        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        do
        {
            Console.Write("\nUser > ");

            // Collect user input
            user_input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                exit_chat = true;
                break;
            }

            // check if it's a AZURE AI agent
            if (agent is Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent chatAgent)
            {
                // create Thread
                AgentThread my_thread = await sk_project_client.Client.GetAgentsClient().CreateThreadAsync();

                await chatAgent.AddChatMessageAsync(
                    threadId: my_thread.Id,
                    message: new ChatMessageContent(AuthorRole.User, user_input));

                try
                {
                    bool isCode = false;
                    //await foreach (ChatMessageContent response in chatAgent.InvokeAsync(threadId: my_thread.Id))
                    await foreach (StreamingChatMessageContent response in chatAgent.InvokeStreamingAsync(threadId: my_thread.Id))
                    {
                        if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                        {
                            Console.WriteLine();
                            isCode = !isCode;
                        }
                        // Display response.
                        Console.Write($"{response.Content}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                    exit_chat = true;
                }
                finally
                {
                    Console.WriteLine($"\nDeleting thread {my_thread.Id}...");

                    await Task.WhenAll(
                        [
                            sk_project_client.Client.GetAgentsClient().DeleteThreadAsync(threadId: my_thread.Id)
                        ]);
                }
            }
        } while (!exit_chat);
    }
*/

    // Helper function to create a Bing tool
    private static async Task<List<Azure.AI.Projects.BingGroundingToolDefinition>> CreateBingToolsAsync(
        Azure.AI.Projects.AIProjectClient sk_project_client,
        string bing_connection_name)
    {
        if (string.IsNullOrWhiteSpace(bing_connection_name))
        {
            return null;
        }

        Azure.AI.Projects.ConnectionsClient cxnClient = sk_project_client.GetConnectionsClient();
        Azure.AI.Projects.ConnectionResponse bingConnection = (await cxnClient.GetConnectionAsync(bing_connection_name)).Value;

        var connectionList = new Azure.AI.Projects.ToolConnectionList
        {
            ConnectionList = { new Azure.AI.Projects.ToolConnection(bingConnection.Id) }
        };
        var bingGroundingTool = new Azure.AI.Projects.BingGroundingToolDefinition(connectionList);
        var tools = new List<Azure.AI.Projects.BingGroundingToolDefinition>{bingGroundingTool};
        return tools;
    }

    // Helper function to create a **SEMANTIC KERNEL** AI Foundry agent object
    private static async Task<Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent> CreateAiFoundryAgentAsync(
        Azure.AI.Projects.AIProjectClient sk_project_client,
        string agent_name,
        string deployment_name,
        string connectedresource_name)
    {

        // Semantic Kernel AGENTS client
        Azure.AI.Projects.AgentsClient sk_agents_client = sk_project_client.GetAgentsClient();

        // Semantic Kernel AGENT definition
        Azure.AI.Projects.Agent sk_agent_definition = await sk_agents_client.CreateAgentAsync(
            model: deployment_name,
            name: agent_name,
            description: agent_name,
            instructions: await ReadAgentInstructionsAsync(agentName: agent_name)
        );

        // Create Semantic Kernel AGENT, based on the agent definition (called "model" in this call)
        var sk_agent = new Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent(
            model:sk_agent_definition, client:sk_agents_client);

        return sk_agent;
    }

}