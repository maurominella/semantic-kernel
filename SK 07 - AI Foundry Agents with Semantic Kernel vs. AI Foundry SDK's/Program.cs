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

        // Create the Chat Completion Agent: "Joker", with simple kernel
        Console.WriteLine("\n\nTest the single Agent \"AnimalPicker\" (an AI Foundry Agent), until you enter <exit>");
        // var skAnimalPickerAgent = await CreateAIFoundryAgentAsync(agent_name: "AnimalPicker", aiSettings.GetVariable("PROJECT_CONNECTION_STRING"));


        // Semantic Kernel client for the AI Foundry Project
        AzureAIClientProvider sk_project_client = AzureAIClientProvider.FromConnectionString(
            connectionString: aiSettings.GetVariable("PROJECT_CONNECTION_STRING"),
            credential: new AzureCliCredential());


        // Azure SDK client for the Azure AI Foundry Agents service
        AgentsClient azureAgentsClient = sk_project_client.Client.GetAgentsClient();


        // Azure SDK client for a loading / creating new Azure AI Foundry agent
        Agent azureAnimalPickerAgent;

        if (string.IsNullOrWhiteSpace(aiagent_id))
        {
            azureAnimalPickerAgent = await azureAgentsClient.CreateAgentAsync(
                model: aiSettings.AzureOpenAI.ChatModelDeployment,
                name: "AnimalPicker",
                instructions: ReadAgentInstructions("AnimalPicker")
            );
        }
        else // aiagent_id is an AI Foundry Agent ID
        {
            azureAnimalPickerAgent = await azureAgentsClient.GetAgentAsync(assistantId: aiagent_id);
        }


        // Semantic Kernel client for the Azure AI Foundry agent
        var skAnimalPickerAgent = new AzureAIAgent(
            model: azureAnimalPickerAgent,
            clientProvider: sk_project_client);


        // create Thread
        AgentThread my_thread = await azureAgentsClient.CreateThreadAsync();


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

                await skAnimalPickerAgent.AddChatMessageAsync(
                    threadId: my_thread.Id,
                    message: new ChatMessageContent(AuthorRole.User, userInput)
                    );

                try
                {
                    bool isCode = false;
                    await foreach (ChatMessageContent response in skAnimalPickerAgent.InvokeAsync(threadId: my_thread.Id))
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
                    azureAgentsClient.DeleteThreadAsync(threadId: my_thread.Id),
                    azureAgentsClient.DeleteAgentAsync(agentId: skAnimalPickerAgent.Id)
                ]);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }


    // Helper function to read agent instructions from file
    private static string ReadAgentInstructions(string agentName)
    {
        string filePath = Path.Combine("agents", $"{agentName}.txt");
        return File.ReadAllText(filePath);
    }

    // Helper function to create an AI Foundry agent
    private static async Task<AzureAIAgent> CreateAIFoundryAgentAsync(string agent_name, string aiproject_connection_string)
    {
        // Semantic Kernel client for the AI Foundry Project
        AzureAIClientProvider sk_project_client = AzureAIClientProvider.FromConnectionString(
            connectionString: aiproject_connection_string,
            credential: new AzureCliCredential());


        // Azure SDK client for the Azure AI Foundry Agents service
        AgentsClient azureAgentsClient = sk_project_client.Client.GetAgentsClient();


        // Azure SDK client for a loading / creating new Azure AI Foundry agent
        Agent azureAnimalPickerAgent;


        if (string.IsNullOrWhiteSpace(aiagent_id))
        {
            azureAnimalPickerAgent = await azureAgentsClient.CreateAgentAsync(
                model: aiproject_connection_string,
                name: "AnimalPicker",
                instructions: ReadAgentInstructions("AnimalPicker")
            );
        }
        else
        {
            azureAnimalPickerAgent = await azureAgentsClient.GetAgentAsync(assistantId: aiagent_id);
        }


        // // Semantic Kernel client for the Azure AI Foundry agent
        var skAnimalPickerAgent = new AzureAIAgent(
            model: azureAnimalPickerAgent,
            clientProvider: sk_project_client);

        return skAnimalPickerAgent;
    }
}