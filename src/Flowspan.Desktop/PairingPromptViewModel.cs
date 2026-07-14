using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Domain;

namespace Flowspan.Desktop;

public sealed class PairingPromptViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly RelayCommand acceptPairingCommand;
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly RelayCommand rejectPairingCommand;
    private readonly DesktopPairingDecisionSource source;
    private string capabilitySummary = "No capabilities selected.";
    private bool disposed;
    private bool grantActivityOffer;
    private bool grantActivityReceive;
    private bool hasPendingPrompt;
    private bool isCodeConfirmed;
    private long lastSequence;
    private string pairingCode = "No code";
    private string pairingExpiresAt = "Not pending";
    private Guid? pairingPromptId;
    private string pairingProtocol = "Not negotiated";
    private string peerDeviceId = "Unavailable";
    private string peerDisplayName = "No peer";
    private string peerFingerprint = "Unavailable";
    private string status =
        "No pairing confirmation is pending. Pairing is unavailable in this build.";

    public PairingPromptViewModel(
        DesktopPairingDecisionSource source,
        IDesktopUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        this.source = source;
        this.dispatcher = dispatcher;
        acceptPairingCommand = new RelayCommand(
            AcceptPairing,
            () => HasPendingPrompt && IsCodeConfirmed);
        rejectPairingCommand = new RelayCommand(
            RejectPairing,
            () => HasPendingPrompt);
        source.PromptChanged += OnPromptChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand AcceptPairingCommand => acceptPairingCommand;

    public bool CanAcceptPairing => HasPendingPrompt && IsCodeConfirmed;

    public string CapabilitySummary
    {
        get => capabilitySummary;
        private set => SetProperty(ref capabilitySummary, value);
    }

    public bool GrantActivityOffer
    {
        get => grantActivityOffer;
        set
        {
            if (SetProperty(ref grantActivityOffer, value))
            {
                UpdateCapabilitySummary();
            }
        }
    }

    public bool GrantActivityReceive
    {
        get => grantActivityReceive;
        set
        {
            if (SetProperty(ref grantActivityReceive, value))
            {
                UpdateCapabilitySummary();
            }
        }
    }

    public bool HasPendingPrompt
    {
        get => hasPendingPrompt;
        private set
        {
            if (SetProperty(ref hasPendingPrompt, value))
            {
                acceptPairingCommand.NotifyCanExecuteChanged();
                rejectPairingCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAcceptPairing));
            }
        }
    }

    public bool IsCodeConfirmed
    {
        get => isCodeConfirmed;
        set
        {
            if (SetProperty(ref isCodeConfirmed, value))
            {
                acceptPairingCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAcceptPairing));
            }
        }
    }

    public string PairingCode
    {
        get => pairingCode;
        private set => SetProperty(ref pairingCode, value);
    }

    public string PairingExpiresAt
    {
        get => pairingExpiresAt;
        private set => SetProperty(ref pairingExpiresAt, value);
    }

    public string PairingProtocol
    {
        get => pairingProtocol;
        private set => SetProperty(ref pairingProtocol, value);
    }

    public string PeerDeviceId
    {
        get => peerDeviceId;
        private set => SetProperty(ref peerDeviceId, value);
    }

    public string PeerDisplayName
    {
        get => peerDisplayName;
        private set => SetProperty(ref peerDisplayName, value);
    }

    public string PeerFingerprint
    {
        get => peerFingerprint;
        private set => SetProperty(ref peerFingerprint, value);
    }

    public ICommand RejectPairingCommand => rejectPairingCommand;

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        source.PromptChanged -= OnPromptChanged;
        source.Dispose();
    }

    private void AcceptPairing()
    {
        if (pairingPromptId is not { } promptId || !IsCodeConfirmed)
        {
            return;
        }

        var capabilities = new List<Capability>(2);
        if (GrantActivityOffer)
        {
            capabilities.Add(Capability.ActivityOffer);
        }

        if (GrantActivityReceive)
        {
            capabilities.Add(Capability.ActivityReceive);
        }

        if (!source.TryAccept(promptId, CapabilityGrant.Of([.. capabilities])))
        {
            Status = "The pairing request is no longer active. No capabilities were granted.";
        }
    }

    private void ApplyPromptChange(DesktopPairingPromptChangedEventArgs eventArgs)
    {
        if (disposed || eventArgs.Sequence <= lastSequence)
        {
            return;
        }

        lastSequence = eventArgs.Sequence;
        if (source.CurrentPrompt is { } prompt)
        {
            bool changedPeer = pairingPromptId != prompt.PromptId;
            pairingPromptId = prompt.PromptId;
            PeerDisplayName = prompt.PeerDisplayName;
            PeerDeviceId = prompt.PeerDeviceId;
            PeerFingerprint = prompt.PeerFingerprint;
            PairingProtocol = prompt.ProtocolVersion;
            PairingCode = $"{prompt.ShortAuthenticationString[..3]} {prompt.ShortAuthenticationString[3..]}";
            PairingExpiresAt = prompt.ExpiresAt
                .ToLocalTime()
                .ToString("g", CultureInfo.CurrentCulture);
            if (changedPeer)
            {
                IsCodeConfirmed = false;
                GrantActivityOffer = false;
                GrantActivityReceive = false;
            }

            HasPendingPrompt = true;
            Status =
                "Compare the code on both devices. Pairing alone does not share an Activity.";
            return;
        }

        pairingPromptId = null;
        HasPendingPrompt = false;
        IsCodeConfirmed = false;
        GrantActivityOffer = false;
        GrantActivityReceive = false;
        PeerDisplayName = "No peer";
        PeerDeviceId = "Unavailable";
        PeerFingerprint = "Unavailable";
        PairingProtocol = "Not negotiated";
        PairingCode = "No code";
        PairingExpiresAt = "Not pending";
        Status = eventArgs.Kind switch
        {
            DesktopPairingPromptChangeKind.Accepted =>
                "Pairing confirmation sent. Waiting for the peer and secure completion.",
            DesktopPairingPromptChangeKind.Rejected =>
                "Pairing rejected. No capabilities were granted.",
            DesktopPairingPromptChangeKind.Canceled =>
                "Pairing ended before confirmation. No capabilities were granted.",
            DesktopPairingPromptChangeKind.Disposed =>
                "Pairing closed locally. No capabilities were granted.",
            _ => "No pairing confirmation is pending.",
        };
    }

    private void OnPromptChanged(
        object? sender,
        DesktopPairingPromptChangedEventArgs eventArgs) =>
        dispatcher.Post(() => ApplyPromptChange(eventArgs));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RejectPairing()
    {
        if (pairingPromptId is { } promptId && !source.TryReject(promptId))
        {
            Status = "The pairing request is no longer active. No capabilities were granted.";
        }
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void UpdateCapabilitySummary()
    {
        CapabilitySummary = (GrantActivityOffer, GrantActivityReceive) switch
        {
            (true, true) => "Allow Activity offers and Activity receives.",
            (true, false) => "Allow this peer to offer Activities.",
            (false, true) => "Allow this peer to receive Activities.",
            _ => "No capabilities selected.",
        };
    }
}
