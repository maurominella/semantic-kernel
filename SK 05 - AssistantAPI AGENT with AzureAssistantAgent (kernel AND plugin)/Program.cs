// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// OpenAIAssistantAgent is the KEY of this exercise 
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// OpenAIAssistantAgent Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet
// OpenAIAssistantFileSearch https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-search?pivots=programming-language-csharp
// GA Migration guide (AI Foundry): https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp

/*
Package: Microsoft.SemanticKernel.Agents.OpenAI (prerelease) - Based on OpenAI Assistant API
- Version: 1.55.0-preview (as of 2025-06-07)
- Source: https://www.nuget.org/packages/Microsoft.SemanticKernel.Agents.OpenAI/1.55.0-preview
- Underlying SDK: 
  - OpenAI or Azure.AI.OpenAI
  - Not GA
- Available on both OpenAI and Azure endpoints
- Open AI announced deprecation by early 2026 (going away)
*/

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --><PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.55.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.AI.OpenAI;
using OpenAI.VectorStores;
using OpenAI.Files;
using OpenAI.Assistants;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel; // does not require Microsoft.SemanticKernel module
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;

// dotnet add package DotNetEnv --> <PackageReference Include="DotNetEnv" Version="3.1.1" />
using DotNetEnv;

// contains the LightsPlugin class
using MyApp.Plugins;

// contains the AISettings class
namespace LLMSettings;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.55.0" />
/*
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using Azure.AI.OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using OpenAI.VectorStores;
*/

internal class Program
{
    private static async Task Main(string[] args)
    {
        #region Environment Configuration
        Console.WriteLine("Application starts");
        // Load configuration from environment variables or user secrets.
        var aiSettings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {aiSettings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {aiSettings.AzureOpenAI.ChatModelDeployment}" +
        $"PROJECT_ENDPOINT: {aiSettings.AzureOpenAI.ProjectEndpoint}");
        #endregion

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Env.GetString("AZURE_OPENAI_ENDPOINT")}\n" +
            $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")}");

        // Instantiation of the Client for Azure OpenAI 
        AzureOpenAIClient azure_openai_client = OpenAIAssistantAgent.CreateAzureOpenAIClient(
            credential: new AzureCliCredential(), endpoint: new Uri(aiSettings.AzureOpenAI.Endpoint));

        // Get the File Client
        OpenAIFileClient fileClient = azure_openai_client.GetOpenAIFileClient();

        // Enumerate the existing files
        IEnumerable<OpenAIFile> uploadedFiles = (await fileClient.GetFilesAsync()).Value;
        Console.WriteLine($"There are {uploadedFiles.Count()} pre-existing files(s)");

        // delete all files
        foreach (var file in uploadedFiles)
        {
            Console.WriteLine($"Deleting File <Id: {file.Id}, Name: {file.Filename}>...");
            await fileClient.DeleteFileAsync(file.Id);
        }

        // Identify and Upload the files to search in
        var s_files_references_to_search_in = new List<OpenAIFile>();
        string[] s_files_to_search_in =
        [
            "data/search_files/trailmaster_product_info_1.md",
        ];
        foreach (string f in s_files_to_search_in)
        {
            OpenAIFile fileInfo = await fileClient.UploadFileAsync(f, FileUploadPurpose.Assistants);
            s_files_references_to_search_in.Add(fileInfo);
            Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
        }

        // Identify and Upload the files to work with
        var s_id_files_to_work_with = new List<string>();
        string[] s_files_to_work_with =
        [
            "data/codeinterpreter_files/turbines.csv",
        ];
        foreach (string f in s_files_to_work_with)
        {
            OpenAIFile fileInfo = await fileClient.UploadFileAsync(f, FileUploadPurpose.Assistants);
            s_id_files_to_work_with.Add(fileInfo.Id);
            Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
        }

        // Get the VectorStore Client
        VectorStoreClient storeClient = azure_openai_client.GetVectorStoreClient();

        // Enumerate the existing vectors stores
        var vectorStores = await storeClient.GetVectorStoresAsync().ToListAsync();

        // Delete all vector stores
        Console.WriteLine($"There are {vectorStores.Count} pre-existing Vector Store(s)");
        foreach (VectorStore v in vectorStores)
        {
            Console.WriteLine($"Deleting Vector Store with Id = <{v.Id}>...");
            await storeClient.DeleteVectorStoreAsync(vectorStoreId: v.Id);
        }

        // Create a Vector Store and add the files to it
        string storeId = (await storeClient.CreateVectorStoreAsync(waitUntilCompleted: true)).VectorStoreId;
        Console.WriteLine($"New Vector Store created with Id = <{storeId}>");

        foreach (OpenAIFile file in s_files_references_to_search_in) // if you want **ALL** files: (await fileClient.GetFilesAsync()).Value)
        {
            Console.WriteLine($"Adding new file <Id: {file.Id}, Name: {file.Filename}> to the <{storeId}> vector store...");
            await storeClient.AddFileToVectorStoreAsync(storeId, file.Id, waitUntilCompleted: true);
        }

        // Get the ASSISTANT CLIENT
        AssistantClient assistant_client = azure_openai_client.GetAssistantClient();

        // Enumerate all assistants
        var assistants = await assistant_client.GetAssistantsAsync().ToListAsync();

        // Delete all assistants
        Console.WriteLine($"There are {assistants.Count} pre-existing assistant(s)");
        foreach (Assistant a in assistants)
        {
            Console.WriteLine($"Deleting Assistant <Id: {a.Id}, Name: {a.Name}>...");
            await assistant_client.DeleteAssistantAsync(a.Id);
        }


        // Create an ASSISTANT (or ASSISTANT DEFINITION)        
        Assistant assistant = await assistant_client.CreateAssistantAsync(
            modelId: aiSettings.AzureOpenAI.ChatModelDeployment,
            name: "mauromi's assistant",
            description: "My first assistant",
            instructions: "You are a clever assistant",
            enableCodeInterpreter: true,
            codeInterpreterFileIds: s_id_files_to_work_with,
            enableFileSearch: true,
            vectorStoreId: storeId);
        Console.WriteLine($"Created new Assistant <{assistant.Name}> with Id <{assistant.Id}>");

        // Create an assistant AGENT
        var assistant_agent = new OpenAIAssistantAgent(definition: assistant, client: assistant_client);

        // Here we just define the AgentThread variable, without creating it
        // AgentThread will be started and returned as part of the response
        AgentThread? agent_thread = null;

        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        do
        {
            Console.Write("\nUser, e.g. 'tell me a joke with no less than 200 words', 'what's the Season Rating for the TrailMaster X4 Tent?', 'what is the earliest Maintenance_Date among the turbine with highest voltage?' > ");

            // Collect user input
            user_input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                exit_chat = true;
                break;
            }

            // Generate the agent response(s)
            await foreach (AgentResponseItem<StreamingChatMessageContent> response in assistant_agent.InvokeStreamingAsync( // streaming version
            // await foreach (AgentResponseItem<ChatMessageContent> response in assistant_agent.InvokeAsync( // non streaming version
                new ChatMessageContent(AuthorRole.User, user_input), thread: agent_thread))
            {
                // Process agent response(s)...
                Console.Write($"{response.Message.Content}");
                agent_thread = response.Thread;
            }

            Console.Write($"\n\nEnter 'Y' inf you want to clear the status, or anything else to keep the thread and its messages alive > ");
            var clear_history = Console.ReadLine();
            // Delete the thread if no longer needed
            if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
            {
                // await agent_thread.DeleteAsync();
                agent_thread = null;
            }

        } while (!exit_chat);

    }
}