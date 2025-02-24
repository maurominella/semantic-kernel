﻿// Copyright (c) Microsoft. All rights reserved.


// https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/GettingStartedWithAgents/AzureAIAgent/Step03_AzureAIAgent_Chat.cs

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.37.0-alpha" />
using Microsoft.SemanticKernel.Agents.OpenAI;

//  dotnet add package Microsoft.SemanticKernel.Agents.AzureAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.37.0-alpha" />
using Microsoft.SemanticKernel.Agents.AzureAI;

using Azure.Identity;

using LLMSettings;

// dotnet add package Azure.AI.Projects --prerelease --> <PackageReference Include="Azure.AI.Projects" Version="1.0.0-beta.3" />
using Azure.AI.Projects;

internal class Program
{
    private static string? aiagent_id = null; //"asst_8tFjVkAnFiqSwukxKypuTdwg"; // asst_8tFjVkAnFiqSwukxKypuTdwg

    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var aiSettings = new AISettings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}");

        Console.Write("\n\nPlease enter the AI Foundry Agent ID to load, or leave it blank to create a new one > ");
        aiagent_id = Console.ReadLine();

        // Semantic Kernel client for the AI Foundry Project
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider sk_project_client = AzureAIClientProvider.FromConnectionString(
            connectionString: aiSettings.GetVariable("PROJECT_CONNECTION_STRING"),
            credential: new AzureCliCredential());

        // Create the "Azure AI SDK object" AI Foundry Agent "AnimalPicker"
        Azure.AI.Projects.Agent? azure_animalpicker_agent = await CreateAgentAsync(
            agent_type: "azure_aifoundry_agent", deployment_name: aiSettings.GetVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            agent_name: "AnimalPicker", sk_project_client: sk_project_client, connectedresource_name: aiSettings.GetVariable("BING_CONNECTION_NAME"))
            as Azure.AI.Projects.Agent;

        // Create the "Semantic Kernel SDK object" AI Foundry Agent "AnimalPicker" using the "Azure AI SDK object" AI Foundry Agent
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent? sk_animalpicker_agent = await CreateAgentAsync(
            agent_type: "sk_aifoundry_agent", azure_aifoundry_agent: azure_animalpicker_agent, sk_project_client: sk_project_client)
            as Microsoft.SemanticKernel.Agents.AzureAI.AzureAIAgent;



        // create Thread
        AgentThread my_thread = await sk_project_client.Client.GetAgentsClient().CreateThreadAsync();

        Console.WriteLine("\n\nTest the single Agent \"AnimalPicker\" (an AI Foundry Agent), until you enter <exit>");

        // Initiate a back-and-forth chat
        bool isComplete = false;
        string? userInput;
        try
        {
            do
            {
                Console.WriteLine();
                Console.Write("User > ");
                // Collect user input
                userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = true;
                    break;
                }

                await sk_animalpicker_agent.AddChatMessageAsync(
                    threadId: my_thread.Id,
                    message: new ChatMessageContent(AuthorRole.User, userInput)
                    );

                try
                {
                    bool isCode = false;
                    await foreach (ChatMessageContent response in sk_animalpicker_agent.InvokeAsync(threadId: my_thread.Id))
                    //await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(threadId: my_thread.Id))
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
                    isComplete = true;
                }

                Console.WriteLine();

            } while (!isComplete);
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Cleaning-up...");
            await Task.WhenAll(
                [
                    sk_project_client.Client.GetAgentsClient().DeleteThreadAsync(threadId: my_thread.Id),
                    sk_project_client.Client.GetAgentsClient().DeleteAgentAsync(agentId: sk_animalpicker_agent.Id)
                ]);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }


    // Helper function to read the agent's instructions based on its name
    private static async Task<string> ReadAgentInstructionsAsync(string agentName)
    {
        string instructions;
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        instructions = await File.ReadAllTextAsync(filePath);
        return instructions;
    }

    // Helper function to create a **SEMANTIC KERNEL** AI Foundry agent object 
    private static async Task<object> CreateAgentAsync(
        string agent_type, string? agent_name = null, string? aiproject_connection_string = null, string? deployment_name = null,
        Microsoft.SemanticKernel.Agents.AzureAI.AzureAIClientProvider? sk_project_client = null, string? connectedresource_name = null,
        Azure.AI.Projects.Agent? azure_aifoundry_agent = null)
    {
#pragma warning disable CS8604 // Possible null reference argument.
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
                    var connectedresource = await sk_project_client.Client.GetConnectionsClient().GetConnectionAsync(connectedresource_name);

                    // Pointer for the Azure SDK AI Foundry Agents service
                    Azure.AI.Projects.AgentsClient azureAgentsClient = sk_project_client.Client.GetAgentsClient();
                    var connectionList = new ToolConnectionList
                    {
                        ConnectionList = { new ToolConnection(connectedresource.Value.Id) }
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
                clientProvider: sk_project_client);
            agent = result;
        }

        return agent;
#pragma warning restore CS8604 // Possible null reference argument.
    }

}