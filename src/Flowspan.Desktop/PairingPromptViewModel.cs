using System.ComponentModel;
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
    private string capabilitySummary = DesktopText.Get(
        "PairingPrompt_NoCapabilities");
    private bool disposed;
    private bool grantActivityOffer;
    private bool grantActivityReceive;
    private bool hasPendingPrompt;
    private bool isCodeConfirmed;
    private long lastSequence;
    private string pairingCode = DesktopText.Get("PairingPrompt_NoCode");
    private string pairingExpiresAt = DesktopText.Get(
        "PairingPrompt_NotPending");
    private Guid? pairingPromptId;
    private string pairingProtocol = DesktopText.Get(
        "PairingPrompt_NotNegotiated");
    private string peerDeviceId = DesktopText.Get("PairingPrompt_Unavailable");
    private string peerDisplayName = DesktopText.Get("PairingPrompt_NoPeer");
    private string peerFingerprint = DesktopText.Get(
        "PairingPrompt_Unavailable");
    private string status = DesktopText.Get("PairingPrompt_InitialStatus");

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
            Status = DesktopText.Get("PairingPrompt_RequestNoLongerActive");
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
            PairingExpiresAt = DesktopText.Format(
                "PairingPrompt_ExpiresAt",
                prompt.ExpiresAt.ToLocalTime());
            if (changedPeer)
            {
                IsCodeConfirmed = false;
                GrantActivityOffer = false;
                GrantActivityReceive = false;
            }

            HasPendingPrompt = true;
            Status = DesktopText.Get("PairingPrompt_CompareCode");
            return;
        }

        pairingPromptId = null;
        HasPendingPrompt = false;
        IsCodeConfirmed = false;
        GrantActivityOffer = false;
        GrantActivityReceive = false;
        PeerDisplayName = DesktopText.Get("PairingPrompt_NoPeer");
        PeerDeviceId = DesktopText.Get("PairingPrompt_Unavailable");
        PeerFingerprint = DesktopText.Get("PairingPrompt_Unavailable");
        PairingProtocol = DesktopText.Get("PairingPrompt_NotNegotiated");
        PairingCode = DesktopText.Get("PairingPrompt_NoCode");
        PairingExpiresAt = DesktopText.Get("PairingPrompt_NotPending");
        Status = eventArgs.Kind switch
        {
            DesktopPairingPromptChangeKind.Accepted =>
                DesktopText.Get("PairingPrompt_Accepted"),
            DesktopPairingPromptChangeKind.Rejected =>
                DesktopText.Get("PairingPrompt_Rejected"),
            DesktopPairingPromptChangeKind.Canceled =>
                DesktopText.Get("PairingPrompt_Canceled"),
            DesktopPairingPromptChangeKind.Disposed =>
                DesktopText.Get("PairingPrompt_Disposed"),
            _ => DesktopText.Get("PairingPrompt_NonePending"),
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
            Status = DesktopText.Get("PairingPrompt_RequestNoLongerActive");
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
            (true, true) => DesktopText.Get("PairingPrompt_AllowOfferAndReceive"),
            (true, false) => DesktopText.Get("PairingPrompt_AllowOffer"),
            (false, true) => DesktopText.Get("PairingPrompt_AllowReceive"),
            _ => DesktopText.Get("PairingPrompt_NoCapabilities"),
        };
    }
}
