// Copyright (c) Microsoft. All rights reserved.

// See https://aka.ms/new-console-template for more information

// Import packages: 
// https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-code?pivots=programming-language-csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using DotNetEnv;
using MyApp.Plugins;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.Identity;

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

internal class Program
{
    private static async Task Main(string[] args)
    {
        string projectRoot;

        Console.WriteLine("Application starts");

        // Get the base directory
        DirectoryInfo baseDirectory = new(AppDomain.CurrentDomain.BaseDirectory);

        // retrieve the project root folder
        if (baseDirectory.Parent.Parent.Name == "bin")
        {
            projectRoot = baseDirectory.Parent.Parent.Parent.FullName;
        }
        else
        {
            projectRoot = baseDirectory.FullName;
        }

        string envFilePath = Path.Combine(projectRoot, "./../../config/credentials_my.env");
        Console.WriteLine($"envFilePath: {envFilePath}");

        // Load the environment variables from the .env file
        Env.Load(envFilePath);

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Env.GetString("AZURE_OPENAI_ENDPOINT")}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")}");

        // Create the kernel builder with the pointer to Azure OpenAI
        var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            endpoint: Env.GetString("AZURE_OPENAI_ENDPOINT"),
            apiKey: Env.GetString("AZURE_OPENAI_API_KEY")
        );

        // Add enterprise loggin components
        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

        // Build the kernel from the builder that already contains ChatCompletionService + Logging services
        Kernel kernel = builder.Build();

        // Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
        kernel.Plugins.AddFromType<LightsPlugin>("Lights");

        // Enable planning
        var openAIPromptExecutionSettings = new OpenAIPromptExecutionSettings()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Create the OpenAI Assistant Agent
        Console.WriteLine("\nDefining Assistant Agent...");
        string agent_name = "agent_name";
        string instructions = "you are a clever agent";

        // OpenAIClientProvider will be used for the Agent Definition as well as file-upload
        var clientProviderForAzure = OpenAIClientProvider.ForAzureOpenAI(
            credential: new AzureCliCredential(),
            endpoint: new Uri(Env.GetString("AZURE_OPENAI_ENDPOINT")));

        var agent =
            await OpenAIAssistantAgent.CreateAsync(
                clientProvider: clientProviderForAzure,
                definition: new OpenAIAssistantDefinition(Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"))
                {
                    Name = agent_name,
                    Instructions = instructions,
                    EnableCodeInterpreter = false,
                    EnableFileSearch = false
                },
                kernel: kernel
                );

        Console.WriteLine("...Assistant Agent is ready.");

        Console.WriteLine("\nCreating thread...");
        string threadId = await agent.CreateThreadAsync();
        Console.WriteLine("...thread was created.\n");

        // Initiate a back-and-forth chat
        bool isComplete = false;
        try
        {
            string? userInput;
            do
            {
                Console.WriteLine();
                Console.Write("User > ");
                // Collect user input
                userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput))
                {
                    continue;
                }
                if (userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = true;
                    break;
                }

                await agent.AddChatMessageAsync(threadId, new ChatMessageContent(AuthorRole.User, userInput));

                try
                {
                    await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(threadId))
                    {
                        // Display response.
                        Console.Write($"{response.Content}");

                        // SK does NOT need to add the response to the thread history
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    isComplete = true;
                    break;
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
                    agent.DeleteThreadAsync(threadId),
                    agent.DeleteAsync()
                ]);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.