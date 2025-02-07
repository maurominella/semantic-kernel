// Copyright (c) Microsoft. All rights reserved.

﻿// See https://aka.ms/new-console-template for more information
// Import packages
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using DotNetEnv;
using MyApp.Plugins;
using Microsoft.SemanticKernel.Agents;

string projectRoot;

Console.WriteLine("Application starts");

// Get the base directory
DirectoryInfo baseDirectory = new(AppDomain.CurrentDomain.BaseDirectory);

// retrieve the project root folder
#pragma warning disable CS8602 // Dereference of a possibly null reference.
if (baseDirectory.Parent.Parent.Name == "bin")
{
    projectRoot = baseDirectory.Parent.Parent.Parent.FullName;
}
else
{
    projectRoot = baseDirectory.FullName;
}
#pragma warning restore CS8602 // Dereference of a possibly null reference.

string envFilePath = Path.Combine(projectRoot, "./../../config/credentials_my.env");
Console.WriteLine($"envFilePath: {envFilePath}");

// Load the environment variables from the .env file
Env.Load(envFilePath);

// Populate values from your OpenAI deployment
var modelId = Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME");
var endpoint = Env.GetString("AZURE_OPENAI_ENDPOINT");
var apiKey = Env.GetString("AZURE_OPENAI_API_KEY");

Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {endpoint}\nAZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {modelId}");

// Create the Azure Chat Completion object, e.g. the pointer to Azure OpenAI
var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);

// Add enterprise components
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

// Build the kernel
Kernel kernel = builder.Build();

// Extract ChatCompletionService from the kernel
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Add a plugin (the LightsPlugin class is defined below)
kernel.Plugins.AddFromType<LightsPlugin>("Lights");

// Enable planning
OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

// Create a history store the conversation
var history = new ChatHistory();


string agent_name = "agent_name";
string instructions = "you are a clever agent";

Console.WriteLine("\nDefining agent...");

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
ChatCompletionAgent agent =
    new()
    {
        Name = agent_name,
        Instructions = instructions,
        Kernel = kernel,
        Arguments =
            new KernelArguments(openAIPromptExecutionSettings)
            {
                { "repository", "microsoft/semantic-kernel" }
            }
    };
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


Console.WriteLine("...agent is ready.");


// Initiate a back-and-forth chat
string? userInput;
do
{
    // Collect user input
    Console.Write("User > ");
    userInput = Console.ReadLine();

    // Check if userInput is not null before adding it to the chat history
    if (userInput != null)
    {
        history.AddUserMessage(userInput);


#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        await foreach (ChatMessageContent response in agent.InvokeAsync(history))
        {
            Console.WriteLine($"{response.Content}");
            // Add the message from the agent to the chat history
            history.AddMessage(response.Role, response.Content ?? string.Empty);
        }
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    }
} while (!userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("Application ends");