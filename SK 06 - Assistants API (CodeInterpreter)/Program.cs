// Copyright (c) Microsoft. All rights reserved.

// See https://aka.ms/new-console-template for more information

// Import packages: 
// https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-code?pivots=programming-language-csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using MyApp.Plugins;
using AgentsSample;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.Identity;
using OpenAI.Files;
using System.Diagnostics;


internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var settings = new Settings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {settings.AzureOpenAI.ChatModelDeployment}");

        // Create the Azure Chat Completion object, e.g. the pointer to Azure OpenAI
        var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: settings.AzureOpenAI.ChatModelDeployment,
            endpoint: settings.AzureOpenAI.Endpoint,
            apiKey: settings.AzureOpenAI.ApiKey
        );

        // Build the kernel, that already integrates the AzureOpenAIChatCompletion object
        Kernel kernel = builder.Build();

        // Add enterprise components
        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

        // Add a plugin (the LightsPlugin class is defined below)
        kernel.Plugins.AddFromType<LightsPlugin>("Lights");

        // Enable planning
        var openAIPromptExecutionSettings = new OpenAIPromptExecutionSettings()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // OpenAIClientProvider will be used for the Agent Definition as well as file-upload
        var clientProvider = OpenAIClientProvider.ForAzureOpenAI(
            credential: new AzureCliCredential(),
            endpoint: new Uri(settings.AzureOpenAI.Endpoint));

        OpenAIFileClient fileClient = clientProvider.Client.GetOpenAIFileClient();


        // Delete existing files
        DeleteAllFiles(fileClient);


        // Upload files
        Console.WriteLine("\nUploading files...");
        OpenAIFile fileDataCountryDetail = await fileClient.UploadFileAsync("./data/PopulationByAdmin1.csv", FileUploadPurpose.Assistants);
        OpenAIFile fileDataCountryList = await fileClient.UploadFileAsync("./data/PopulationByCountry.csv", FileUploadPurpose.Assistants);
        Console.WriteLine("... files uploaded.");


        // Create the OpenAI Assistant Agent
        Console.WriteLine("\nDefining agent...");
        string agent_name = "agent_name";
        string instructions = "you are a clever agent";

        OpenAIAssistantAgent agent =
            await OpenAIAssistantAgent.CreateAsync(
                clientProvider: clientProvider,
                definition: new OpenAIAssistantDefinition(settings.AzureOpenAI.ChatModelDeployment)
                {
                    Name = agent_name,
                    Instructions = instructions,
                    EnableCodeInterpreter = true,
                    EnableFileSearch = false,
                    CodeInterpreterFileIds = [fileDataCountryList.Id, fileDataCountryDetail.Id]
                },
                kernel: kernel
                );
        Console.WriteLine("...agent is ready.");

        Console.WriteLine("\nCreating thread...");
        string threadId = await agent.CreateThreadAsync();
        Console.WriteLine("...thread was created.");

        // Initiate a back-and-forth chat
        bool isComplete = false;
        List<string> fileIds = [];
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
                    bool isCode = false;
                    // await foreach (ChatMessageContent response in agent.InvokeAsync(threadId)) // InvokeStreamingAsync
                    await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(threadId)) 
                    {
                        if (isCode != (response.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false))
                        {
                            Console.WriteLine();
                            isCode = !isCode;
                        }
                        // Display response.
                        Console.Write($"{response.Content}");

                        // Capture file IDs for downloading
                        fileIds.AddRange(response.Items.OfType<StreamingFileReferenceContent>().Select(item => item.FileId));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    isComplete = true;
                    break;
                }
                Console.WriteLine();

                // Download any files referenced in the response
                await DownloadResponseImageAsync(fileClient, fileIds);
                fileIds.Clear();

            } while (!isComplete);
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Cleaning-up...");
            await Task.WhenAll(
                [
                    agent.DeleteThreadAsync(threadId),
                    agent.DeleteAsync(),
                    fileClient.DeleteFileAsync(fileDataCountryList.Id),
                    fileClient.DeleteFileAsync(fileDataCountryDetail.Id)
                ]);
            DeleteAllFiles(fileClient);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }

    private static void DeleteAllFiles(OpenAIFileClient fileClient)
    {
        var all_files = fileClient.GetFiles();
        int i = 0;


        Console.WriteLine("\nStart deleting files...");
        foreach (var file in all_files.Value)
        {
            Console.WriteLine(++i + ". Deleting " + file.Filename + "...");
            fileClient.DeleteFileAsync(file.Id);
        }

        Console.WriteLine("..." + i + " file(s) deleted.");

    }

    private static async Task DownloadResponseImageAsync(OpenAIFileClient client, ICollection<string> fileIds)
    {
        if (fileIds.Count > 0)
        {
            Console.WriteLine();
            foreach (string fileId in fileIds)
            {
                await DownloadFileContentAsync(client, fileId, launchViewer: true);
            }
        }
    }

    private static async Task DownloadFileContentAsync(OpenAIFileClient client, string fileId, bool launchViewer = false)
    {
        OpenAIFile fileInfo = client.GetFile(fileId);
        if (fileInfo.Purpose == FilePurpose.AssistantsOutput)
        {
            string filePath =
                Path.Combine(
                    Path.GetTempPath(),
                    Path.GetFileName(Path.ChangeExtension(fileInfo.Filename, ".png")));

            if (!File.Exists(filePath))
            {
                BinaryData content = await client.DownloadFileAsync(fileId);
                await using FileStream fileStream = new(filePath, FileMode.CreateNew);
                await content.ToStream().CopyToAsync(fileStream);
                Console.WriteLine($"File saved to: {filePath}.");
            }

            if (launchViewer)
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C start {filePath}"
                    });
            }
        }
    }
}