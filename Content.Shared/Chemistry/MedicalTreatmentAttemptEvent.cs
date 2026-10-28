namespace Content.Shared.Chemistry;

/// <summary>Raised on a patient before medical treatment or chemical effects are applied.</summary>
[ByRefEvent]
public record struct MedicalTreatmentAttemptEvent(bool Cancelled = false);
