using System;
using System.Numerics;
using System.Collections.Generic;
using Robust.Shared.Utility;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Misfits.Holotape;

public sealed partial class HolotapeWindow
{
    private bool _commandMode;
    private string? _gameProgram;
    private int _scrollHistoryFrames;
    private readonly BoxContainer _commandPanel = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
    private readonly RichTextLabel _commandOutput = new() { HorizontalExpand = true };
    private readonly Queue<string> _commandHistory = new();
    private readonly ScrollContainer _commandScroll = new() { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
    private readonly LineEdit _commandInput = new() { HorizontalExpand = true };
    private readonly BoxContainer _gamePanel = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
    private readonly Label _gameStatus = new() { FontColorOverride = Color.FromHex("#33FF33") };
    private readonly Label _gameInstructions = new() { FontColorOverride = Color.FromHex("#33FF33") };
    private Robust.Client.UserInterface.Control? _terminalGame;

    private void InitializeCommandPrompt()
    {
        var prompt = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        prompt.AddChild(new Label { Text = ">", FontColorOverride = Color.FromHex("#33FF33") });
        prompt.AddChild(_commandInput); // LineEdit supplies the blinking input caret.
        _commandScroll.AddChild(_commandOutput);
        _commandPanel.AddChild(_commandScroll);
        OnClose += () =>
        {
            CloseTerminalGame();
            _commandHistory.Clear();
            _commandOutput.SetMessage(string.Empty);
            _commandInput.Text = string.Empty;
        };
        _commandPanel.AddChild(prompt);
        _commandInput.OnTextEntered += _ => RunTerminalCommand();
        InitializeDesktop();
        _gamePanel.AddChild(_gameStatus);
        _gamePanel.AddChild(_gameInstructions);
    }

    public void SetCommandPrompt(bool enabled)
    {
        if (_commandMode == enabled)
            return;
        _commandMode = enabled;
        if (!enabled)
        {
            CloseTerminalGame();
            DataPanel.RemoveChild(_commandPanel);
            DataPanel.RemoveChild(_gamePanel);
            foreach (var child in DataPanel.Children) child.Visible = true;
            HeaderLabel.Visible = true;
            DataTabButton.Text = "[ DATA ]";
            return;
        }
        foreach (var child in DataPanel.Children)
            child.Visible = false;
        DataPanel.AddChild(_commandPanel);
        DataPanel.AddChild(_gamePanel);
        DataTabButton.Text = "[ COMMAND ]";
        HeaderLabel.Visible = false;
        _commandInput.GrabKeyboardFocus();
    }

    public void SetGameProgram(string? program)
    {
        if (_gameProgram != program) CloseTerminalGame();
        _gameProgram = program;
        RefreshDesktopApps();
    }

    private void RunTerminalCommand()
    {
        var command = _commandInput.Text.Trim();
        if (command.Length > 128) command = command[..128];
        _commandInput.Text = string.Empty;
        if (command.Length == 0)
            return;
        AppendCommandHistory("> " + command);
        switch (command.ToLowerInvariant())
        {
            case "hi":
            case "hello":
                AppendCommandHistory("hello world!");
                break;
            case "vault carnage":
            case "red menace":
            case "atomic defense":
            case "zetan siege":
            case "vault runner":
            case "wasteland quest":
            case "robot revolt":
                if (!string.Equals(command, _gameProgram, StringComparison.OrdinalIgnoreCase))
                {
                    AppendCommandHistory("Command not recognized");
                    break;
                }
                _commandPanel.Visible = false;
                _gamePanel.Visible = true;
                _terminalGame = _gameProgram switch
                {
                    "vault carnage" => new RedMenaceControl(),
                    "wasteland quest" => new WastelandQuestControl(),
                    _ => new HolotapeArcadeControl(_gameProgram!),
                };
                _gameInstructions.Text = _gameProgram == "wasteland quest"
                    ? "Choose two companions, then use the story buttons. Escape quits without saving."
                    : "WASD/arrows: move or aim | Space/click: action | Esc: quit | Vault Carnage: 1-4 guns, R reload";
                _terminalGame.HorizontalExpand = true;
                _terminalGame.VerticalExpand = true;
                _terminalGame.MinSize = new Vector2(480,320);
                var game = (IHolotapeGame)_terminalGame;
                game.OnQuit += CloseTerminalGame;
                game.OnStatus += text => _gameStatus.Text = text;
                _gamePanel.AddChild(_terminalGame);
                game.Restart();
                _terminalGame.GrabKeyboardFocus();
                break;
            case "notes": SwitchToNotesTab(); break;
            case "database": SwitchToDatabaseTab(); break;
            case "help": AppendCommandHistory("hello / hi\nnotes\ndatabase\n" + (_gameProgram != null ? _gameProgram+"\n" : string.Empty) + "clear"); break;
            case "clear": _commandHistory.Clear(); _commandOutput.SetMessage(string.Empty); break;
            default: AppendCommandHistory("Command not recognized"); break;
        }
    }

    private void AppendCommandHistory(string text)
    {
        _commandHistory.Enqueue(text);
        while (_commandHistory.Count > 100) _commandHistory.Dequeue();
        var message = new FormattedMessage();
        message.PushColor(Color.FromHex("#33FF33"));
        message.AddText(string.Join("\n", _commandHistory));
        message.Pop();
        _commandOutput.SetMessage(message); // Plain text, never execute or interpret typed markup.
        _scrollHistoryFrames = 2;
    }

    private void CloseTerminalGame()
    {
        if (_terminalGame == null)
            return;
        _gamePanel.RemoveChild(_terminalGame);
        _terminalGame = null; // No game progress is stored or sent to the server.
        _gamePanel.Visible = false;
        _commandPanel.Visible = true;
        _commandInput.GrabKeyboardFocus();
    }
}
