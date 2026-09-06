using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WidgetHost.Config;
using WidgetHost.Shell;
using WidgetHost.Startup;
using WidgetHost.Widgets;
using Widgets.Abstractions;

namespace WidgetHost;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\WindowsWidgets.SingleInstance";

    private Mutex? _instanceMutex;
    private SettingsStore _store = null!;
    private HostSettings _settings = null!;
    private TrayIcon? _tray;
    private readonly List<WidgetController> _controllers = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Já tem um host rodando; sair sem barulho.
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Sem janela principal: o app vive na bandeja e só morre quando mandado.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnUnhandledException;

        _store = new SettingsStore();
        _settings = _store.Load();

        _tray = new TrayIcon(WidgetCatalog.Types);
        _tray.RefreshAllRequested += (_, _) => _controllers.ForEach(c => c.Widget.Refresh());
        _tray.SettingsRequested += (_, _) => _controllers.FirstOrDefault()?.Widget.ShowSettings();
        _tray.AddWidgetRequested += (_, type) => AddWidget(type);
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.StartWithWindowsChanged += OnStartWithWindowsChanged;

        // O registro é a fonte de verdade, não o JSON.
        _tray.SetStartupChecked(StartupRegistrar.IsEnabled());

        StartSavedWidgets();
    }

    private void StartSavedWidgets()
    {
        // Primeira execução: coloca uma instância de cada tipo para o usuário
        // não encarar uma área de trabalho vazia sem saber que o app subiu.
        if (_settings.Widgets.Count == 0)
        {
            foreach (var type in WidgetCatalog.Types) AddWidget(type);
            return;
        }

        foreach (var instance in _settings.Widgets.ToList())
        {
            var type = WidgetCatalog.Find(instance.Type);
            if (type is null)
            {
                // Tipo sumiu do catálogo (versão antiga do app). A entrada fica
                // no JSON: remover apagaria a configuração do usuário à toa.
                Debug.WriteLine($"[App] tipo desconhecido '{instance.Type}', instância ignorada");
                continue;
            }

            StartInstance(type, instance);
        }

        Persist();
    }

    private void AddWidget(WidgetType type)
    {
        var instance = new WidgetInstanceSettings
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            Type = type.Id,
        };

        _settings.Widgets.Add(instance);
        StartInstance(type, instance);
        Persist();
    }

    private void StartInstance(WidgetType type, WidgetInstanceSettings instance)
    {
        var widget = type.Create(instance.Options, json =>
        {
            instance.Options = json;
            Persist();
        });

        var controller = new WidgetController(
            widget, instance, Persist, cascadeIndex: _controllers.Count);

        controller.Closed += (sender, _) => RemoveWidget((WidgetController)sender!);

        controller.Start();
        _controllers.Add(controller);

        _tray?.SetAttachMode(controller.Mode);
    }

    private void RemoveWidget(WidgetController controller)
    {
        _controllers.Remove(controller);
        _settings.Widgets.Remove(controller.Settings);
        controller.Dispose();
        Persist();
    }

    private void OnStartWithWindowsChanged(object? sender, bool enabled)
    {
        if (!StartupRegistrar.SetEnabled(enabled))
        {
            MessageBox.Show(
                "Não foi possível alterar o início automático. Verifique as permissões do registro.",
                "Windows Widgets", MessageBoxButton.OK, MessageBoxImage.Warning);
            _tray?.SetStartupChecked(StartupRegistrar.IsEnabled());
            return;
        }

        _settings.StartWithWindows = enabled;
        Persist();
    }

    private void Persist() => _store.Save(_settings);

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Um widget quebrado não pode derrubar o host inteiro.
        Debug.WriteLine($"[App] exceção não tratada: {e.Exception}");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var controller in _controllers) controller.Dispose();
        _controllers.Clear();

        _tray?.Dispose();

        if (_store is not null && _settings is not null) Persist();

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
