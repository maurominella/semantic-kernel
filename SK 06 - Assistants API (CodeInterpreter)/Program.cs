﻿// Copyright (c) Microsoft. All rights reserved.

// See https://aka.ms/new-console-template for more information

// Import packages as shown with the following sample: 
// https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-code?pivots=programming-language-csharp
using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents.OpenAI;
using OpenAI.Files;
using AgentsSample;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Application starts");

        // Load configuration from environment variables or user secrets.
        var settings = new Settings();

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {settings.AzureOpenAI.ChatModelDeployment}");

        // Build the kernel WITHOUT using the builder since SK uses OpenAIClientProvider
        var kernel = new Kernel(); // builder.Build();

        // Add enterprise logging components
        // TBI


        // OpenAIClientProvider will be used for the Agent Definition as well as file-upload
        var clientProviderForAzure = OpenAIClientProvider.ForAzureOpenAI(
            apiKey: new System.ClientModel.ApiKeyCredential(settings.AzureOpenAI.ApiKey),
            endpoint: new Uri(settings.AzureOpenAI.Endpoint));

        // create a pointer to the file client provider
        OpenAIFileClient fileClient = clientProviderForAzure.Client.GetOpenAIFileClient();

        // Delete existing files
        DeleteAllFiles(fileClient);

        // Upload files
        Console.WriteLine("\nUploading files...");
        OpenAIFile fileDataCountryDetail = fileClient.UploadFile("./data/PopulationByAdmin1.csv", FileUploadPurpose.Assistants);
        OpenAIFile fileDataCountryList = fileClient.UploadFile("./data/PopulationByCountry.csv", FileUploadPurpose.Assistants);
        Console.WriteLine("...files were successfully uploaded.");

        // Create the OpenAI Assistant Agent
        Console.WriteLine("\nDefining Assistant Agent...");
        string agent_name = "mauromi_assistant_agent_c#";
        string instructions = "you are a clever agent";

        OpenAIAssistantAgent agent =
            await OpenAIAssistantAgent.CreateAsync(
                clientProvider: clientProviderForAzure,
                definition: new OpenAIAssistantDefinition(settings.AzureOpenAI.ChatModelDeployment)
                {
                    Name = agent_name,
                    Instructions = instructions,
                    EnableCodeInterpreter = true,
                    EnableFileSearch = false,
                    CodeInterpreterFileIds = [fileDataCountryList.Id, fileDataCountryDetail.Id]
                },
                kernel: kernel // empty kernel, with no associated plugins nor services
                );
        Console.WriteLine("...Assistant Agent is ready.");

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
                if (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = true;
                    break;
                }

                await agent.AddChatMessageAsync(threadId, new ChatMessageContent(AuthorRole.User, userInput));

                try
                {
                    bool isCode = false;
                    //await foreach (ChatMessageContent response in agent.InvokeAsync(threadId)) // InvokeStreamingAsync
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
                    Console.WriteLine($"Error (but don't worry, we can continue ;-)): {ex.Message}");
                    isComplete = true;
                }

                fileIds = RemoveDuplicates(fileIds);
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
            fileClient.DeleteFile(file.Id);
        }

        Console.WriteLine("..." + i + " file(s) deleted.");

    }
    // Helper function to remove duplicates from a string list, when it's built by a streaming function
    private static List<string> RemoveDuplicates(List<string> fileIds)

    {
        // Using HashSet to remove duplicates
        var uniqueFileIds = new HashSet<string>(fileIds);

        // Converting HashSet back to List
        return uniqueFileIds.ToList();
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