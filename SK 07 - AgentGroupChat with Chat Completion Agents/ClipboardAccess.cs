// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.SemanticKernel;

namespace LLMClipboardAccess;

public class ClipboardAccess
{
    [KernelFunction]
    [Description("Copies the provided content to the clipboard.")]
    public static void SetClipboard(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        using Process clipProcess = Process.Start(
            new ProcessStartInfo
            {
                FileName = "clip",
                RedirectStandardInput = true,
                UseShellExecute = false,
            });
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        clipProcess.StandardInput.Write(content);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        clipProcess.StandardInput.Close();
    }
}