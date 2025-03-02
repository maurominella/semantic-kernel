﻿// Copyright (c) Microsoft. All rights reserved.


// https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/GettingStartedWithAgents/AzureAIAgent/Step03_AzureAIAgent_Chat.cs

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.39.0-alpha" />
using Microsoft.SemanticKernel.Agents.OpenAI;

//  dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.39.0-alpha" />
using Microsoft.SemanticKernel.Agents.AzureAI;

using Azure.Identity;

using LLMSettings;

// dotnet add package Azure.AI.Projects --prerelease --> <PackageReference Include="Azure.AI.Projects" Version="1.0.0-beta.3" />
using Azure.AI.Projects;

// dotnet add package Microsoft.SemanticKernel.Connectors.AzureOpenAI --> <PackageReference Include="Microsoft.SemanticKernel.Connectors.AzureOpenAI" Version="1.39.0" />

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

        // Semantic Kernel client for the AI Foundry Project
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider sk_project_client = AzureAIClientProvider.FromConnectionString(
            connectionString: aiSettings.GetVariable("PROJECT_CONNECTION_STRING"),
            credential: new AzureCliCredential());

        // Semantic Kernel AI Foundry Agent
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent sk_animalpicker_agent = await CreateAiFoundryAgentAsync(
            sk_project_client: sk_project_client,
            agent_name: "AnimalPicker",
            deployment_name: aiSettings.GetVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            connectedresource_name: aiSettings.GetVariable("BING_CONNECTION_NAME"));

        // Test the AnimalPicker agent
        Console.WriteLine("\n\nTest the single Agent \"AnimalPicker\" (an AI Foundry Agent), until you enter <exit>");
        await ChatWithAgentAsync(agent: sk_animalpicker_agent, sk_project_client: sk_project_client);
        Console.WriteLine($"Deleting the agent {sk_animalpicker_agent.Name} ({sk_animalpicker_agent.Id})...");
        await sk_project_client.Client.GetAgentsClient().DeleteAgentAsync(agentId: sk_animalpicker_agent.Id);

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
    private static async Task<object> CreateAgentAsync(
        string agent_type, string? agent_name = null, string? aiproject_connection_string = null, string? deployment_name = null,
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider? sk_project_client = null, string? connectedresource_name = null,
        Azure.AI.Projects.Agent? azure_aifoundry_agent = null)
    {
        // There are two options to create the Azure AI Foundry Agent
        // - to create an Azure AI Foundry SDK object, we can directly create it with the Azure AI Foundry SDK
        // - to create a Semantic Kernel SDK object, two steps are needed: Azure AI Foundry SDK object + SK object that relies on the Azure SDK object

        object? agent = null;

        if (agent_type == "azure_aifoundry_agent")
        {
            if (string.IsNullOrWhiteSpace(aiagent_id)) // create the agent
            {
                var tools = new List<ToolDefinition>();

                if (connectedresource_name != null)
                {
                    var connected_resource = await sk_project_client.Client.GetConnectionsClient().GetConnectionAsync(connectedresource_name);

                    // Pointer for the Azure SDK AI Foundry Agents service
                    Azure.AI.Projects.AgentsClient azureAgentsClient = sk_project_client.Client.GetAgentsClient();
                    var connectionList = new ToolConnectionList
                    {
                        ConnectionList = { new ToolConnection(connected_resource.Value.Id) }
                    };

                    var bingGroundingTool = new BingGroundingToolDefinition(connectionList);
                    tools.Add(bingGroundingTool);
                }

                var result = await sk_project_client.Client.GetAgentsClient().CreateAgentAsync(
                    model: deployment_name,
                    name: agent_name,
                    instructions: await ReadAgentInstructionsAsync(agent_name),
                    tools: tools
                );
                agent = result.Value;
            }
            else // load an existing agent whose id = aiagent_id
            {
                var response = await sk_project_client.Client.GetAgentsClient().GetAgentAsync(assistantId: aiagent_id);
                agent = response.Value;
            }
        }

        else if (agent_type == "sk_aifoundry_agent")
        {

            // Semantic Kernel SDK Agent. WE NEED TO "CLONE" THE AZURE AI AGENT TO CREATE THIS!!
            var result = new Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent(
                model: azure_aifoundry_agent,
                client: sk_project_client.Client.GetAgentsClient());
            agent = result;
        }

        return agent;
    }


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


    // Helper function to create a **SEMANTIC KERNEL** AI Foundry agent object
    private static async Task<Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent> CreateAiFoundryAgentAsync(
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider sk_project_client,
        string agent_name,
        string deployment_name,
        string connectedresource_name)
    {
        // Create the "Azure AI SDK object" AI Foundry Agent "AnimalPicker"
        Azure.AI.Projects.Agent? azure_aifoundry_agent = await CreateAgentAsync(
            agent_type: "azure_aifoundry_agent", deployment_name: deployment_name,
            agent_name: agent_name, sk_project_client: sk_project_client, connectedresource_name: connectedresource_name)
            as Azure.AI.Projects.Agent;

        // Create the "Semantic Kernel SDK object" AI Foundry Agent "AnimalPicker" using the "Azure AI SDK object" AI Foundry Agent
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent? sk_animalpicker_agent = await CreateAgentAsync(
            agent_type: "sk_aifoundry_agent", azure_aifoundry_agent: azure_aifoundry_agent, sk_project_client: sk_project_client)
            as Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent;

        return sk_animalpicker_agent;
    }

}