﻿using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Injectio.Attributes;
using StabilityMatrix.Avalonia.Controls;
using StabilityMatrix.Avalonia.Helpers;
using StabilityMatrix.Avalonia.Models;
using StabilityMatrix.Core.Helper;

namespace StabilityMatrix.Avalonia.Views;

[RegisterSingleton<LaunchPageView>]
public partial class LaunchPageView : UserControlBase
{
    private const int LineOffset = 5;

    public LaunchPageView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        TextEditorConfigs.Configure(Console, TextEditorPreset.Console);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        EventManager.Instance.ScrollToBottomRequested -= OnScrollToBottomRequested;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        EventManager.Instance.ScrollToBottomRequested += OnScrollToBottomRequested;
    }

    private void OnScrollToBottomRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var editor = this.FindControl<TextEditor>("Console");
            if (editor?.Document == null)
                return;
            var line = Math.Max(editor.Document.LineCount - LineOffset, 1);
            editor.ScrollToLine(line);
        });
    }
}
