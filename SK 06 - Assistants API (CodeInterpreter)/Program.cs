﻿// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// OpenAIAssistantAgent is the KEY of this exercise 
// Migration guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide?pivots=programming-language-csharp
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.44.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.44.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;

using OpenAI.Files;

using AgentsSample;
using OpenAI.Assistants;
using Azure.AI.OpenAI;
using Azure.Identity;
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

        // Instantiation of the Client for Azure OpenAI 
        AzureOpenAIClient openaiClient = OpenAIAssistantAgent.CreateAzureOpenAIClient(
            new AzureCliCredential(), new Uri(settings.AzureOpenAI.Endpoint));

        // Upload files
        Console.WriteLine("\nUploading files...");
        OpenAIFileClient fileClient = openaiClient.GetOpenAIFileClient();
        OpenAIFile fileDataCountryDetail = await fileClient.UploadFileAsync("./data/PopulationByAdmin.csv", FileUploadPurpose.Assistants);
        OpenAIFile fileDataCountryList = await fileClient.UploadFileAsync("./data/PopulationByCountry.csv", FileUploadPurpose.Assistants);
        Console.WriteLine("...files were successfully uploaded.");

#pragma warning disable OPENAI001 // 'OpenAI.Assistants.AssistantClient' is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        // Using the Azure OpenAI Client, now extract another Client for OpenAI Assistant Agent
        AssistantClient assistantClient = openaiClient.GetAssistantClient();
#pragma warning restore OPENAI001

        // using the Client for OpenAI Assistant Agent, now either...
        // ...EXTRACT the definition for a specific EXISTING OpenAI Assistant:
        // var assistantDefinition = await assistantClient.GetAssistantAsync(assistantId: "");

        //... or CREATE the definition for a NEW OpenAI Assistant:
        string agent_name = "mauromi_assistant_agent_c#";
        string instructions = "you are a clever agent";

        var assistantDefinition = await assistantClient.CreateAssistantAsync(
            modelId: settings.AzureOpenAI.ChatModelDeployment,
            name: agent_name,
            instructions: instructions,
            enableCodeInterpreter: true // CODE INTERPRETER!
        );

        // using the definition of a specific (new or existing) OpenAI Assistant, now we may directly instantiate an OpenAIAssistantAgent 
        var agent = new OpenAIAssistantAgent(definition: assistantDefinition, client: assistantClient);


        // Initiate a back-and-forth chat
        bool isComplete = false;
        List<string> fileIds = [];
        try
        {
            string? userInput;
            do
            {
                Console.WriteLine();
                Console.WriteLine("Query example: create a 3D pie chart with the top 6 countries by population in Europe, showing absolute numbers");
                Console.Write("User > ");

                // Collect user input
                userInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = true;
                    break;
                }

                var message = new ChatMessageContent(AuthorRole.User, userInput);

                try
                {
                    bool isCode = false;
                    //await foreach (ChatMessageContent response in agent.InvokeAsync(message: message)) // InvokeStreamingAsync
                    await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(message: message))
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
                    assistantClient.DeleteAssistantAsync(agent.Id),
                ]);
            await DeleteAllFilesAsync(fileClient);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }

    private static async Task DeleteAllFilesAsync(OpenAIFileClient fileClient)
    {
        var all_files = await fileClient.GetFilesAsync();

        Console.WriteLine("\nStart deleting files...");
        int i = 0;
        foreach (var file in all_files.Value)
        {
            Console.WriteLine($"{++i}. Deleting file {file.Filename} ({file.Id})...");
            await fileClient.DeleteFileAsync(file.Id);
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