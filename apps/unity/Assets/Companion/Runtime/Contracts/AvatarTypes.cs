using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public sealed class AvatarParameter
    {
        public string Id { get; }
        public float Minimum { get; }
        public float Maximum { get; }
        public float DefaultValue { get; }

        public AvatarParameter(string id, float minimum, float maximum, float defaultValue)
        {
            Id = ContractCopy.Required(id, nameof(id));
            Minimum = minimum;
            Maximum = maximum;
            DefaultValue = defaultValue;
        }
    }

    public sealed class AvatarCapabilities
    {
        public string CharacterId { get; }
        public string ModelAssetId { get; }
        public IReadOnlyList<AvatarParameter> Parameters { get; }
        public bool SupportsMouth { get; }
        public bool SupportsBlink { get; }
        public bool SupportsBreath { get; }
        public bool SupportsGaze { get; }
        public IReadOnlyList<Emotion> Expressions { get; }
        public IReadOnlyList<string> MotionIds { get; }

        public AvatarCapabilities(string characterId, string modelAssetId, IReadOnlyList<AvatarParameter> parameters, bool supportsMouth, bool supportsBlink, bool supportsBreath, bool supportsGaze, IReadOnlyList<Emotion> expressions, IReadOnlyList<string> motionIds)
        {
            CharacterId = ContractCopy.Required(characterId, nameof(characterId));
            ModelAssetId = ContractCopy.Required(modelAssetId, nameof(modelAssetId));
            Parameters = ContractCopy.List(parameters, 1024);
            SupportsMouth = supportsMouth;
            SupportsBlink = supportsBlink;
            SupportsBreath = supportsBreath;
            SupportsGaze = supportsGaze;
            Expressions = ContractCopy.List(expressions, 5);
            MotionIds = ContractCopy.List(motionIds, 256);
        }
    }

    public sealed class AvatarActionContext
    {
        public OperationKey Operation { get; }
        public Guid? TurnId { get; }

        public AvatarActionContext(OperationKey operation, Guid? turnId)
        {
            Operation = operation;
            TurnId = turnId;
        }
    }

    public sealed class AvatarActionResult
    {
        public AvatarActionStatus Status { get; }
        public bool UsedNeutralFallback { get; }
        public PreviewError Error { get; }

        public AvatarActionResult(AvatarActionStatus status, bool usedNeutralFallback, PreviewError error)
        {
            Status = status;
            UsedNeutralFallback = usedNeutralFallback;
            Error = error;
        }
    }

    public sealed class AvatarLoadResult
    {
        public Guid LoadRequestId { get; }
        public string CharacterId { get; }
        public AvatarCapabilities Capabilities { get; }
        public PreviewError Error { get; }
        public bool Succeeded => Capabilities != null;

        public AvatarLoadResult(Guid loadRequestId, string characterId, AvatarCapabilities capabilities, PreviewError error)
        {
            if ((capabilities == null) == (error == null)) throw new ArgumentException("Load result needs either capabilities or an error.");
            LoadRequestId = loadRequestId;
            CharacterId = ContractCopy.Required(characterId, nameof(characterId));
            Capabilities = capabilities;
            Error = error;
        }
    }
}
