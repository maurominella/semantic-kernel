// Copyright (c) Microsoft. All rights reserved.

// sample: https://learn.microsoft.com/en-us/azure/ai-services/agents/how-to/tools/bing-grounding?tabs=csharp&pivots=code-example

using Azure;
using Azure.AI.Projects;
using Azure.Identity;

namespace LLMSettings;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");
        // Load configuration from environment variables or user secrets.
        var aiSettings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}");


        // Step 1: Create an agent with Grounding with Bing Search
        var connectionString = aiSettings.AzureAICONNECTIONSTRING;
        var clientOptions = new AIProjectClientOptions();
        var projectClient = new AIProjectClient(connectionString, new DefaultAzureCredential(), clientOptions);


        // Step 2: Enable the Grounding with Bing search tool
        var bingConnection = await projectClient.GetConnectionsClient().GetConnectionAsync(aiSettings.BINGCONNECTIONNAME);
        var connectionId = bingConnection.Value.Id;
        var connectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(connectionId) }
        };
        var bingGroundingTool = new BingGroundingToolDefinition(connectionList);

        AgentsClient agentClient = projectClient.GetAgentsClient();

        Response<Agent> agentResponse = await agentClient.CreateAgentAsync(
            model: aiSettings.AzureOpenAI.ChatModelDeployment,
            name: "my-assistant",
            instructions: "You are a helpful assistant.",
            tools: new List<ToolDefinition> { bingGroundingTool });

        Agent agent = agentResponse.Value;


        // Step 3: Create a thread
        // Create thread for communication
        Response<AgentThread> threadResponse = await agentClient.CreateThreadAsync();
        AgentThread thread = threadResponse.Value;


        // Create message to thread
        Response<ThreadMessage> messageResponse = await agentClient.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            "How does wikipedia explain Euler's Identity?");
        ThreadMessage message = messageResponse.Value;

        // Step 4: Create a run and check the output
        // Run the agent
        Response<ThreadRun> runResponse = await agentClient.CreateRunAsync(thread, agent);
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            runResponse = await agentClient.GetRunAsync(thread.Id, runResponse.Value.Id);
        }
        while (runResponse.Value.Status == RunStatus.Queued
            || runResponse.Value.Status == RunStatus.InProgress);

        Response<PageableList<ThreadMessage>> afterRunMessagesResponse
            = await agentClient.GetMessagesAsync(thread.Id);
        IReadOnlyList<ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

        // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
        foreach (ThreadMessage threadMessage in messages)
        {
            Console.Write($"\n\n{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}:");
            foreach (MessageContent contentItem in threadMessage.ContentItems)
            {
                if (contentItem is MessageTextContent textItem)
                {
                    Console.Write($"\n{textItem.Text}");
                }
                else if (contentItem is MessageImageFileContent imageFileItem)
                {
                    Console.Write($"\n<image from ID: {imageFileItem.FileId}");
                }
                Console.WriteLine();
            }
        }
    }
}