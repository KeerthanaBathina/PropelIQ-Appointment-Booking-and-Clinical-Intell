namespace UPACIP.Service.Calibration;

/// <summary>
/// Static utility implementing Platt scaling for AI confidence score calibration (US_073 AC-1).
///
/// <para>
/// Platt scaling maps a raw model score <c>s</c> to a calibrated probability
/// <c>P(y=1|s)</c> via the sigmoid:
/// </para>
/// <code>
///   P = 1 / (1 + exp(A * s + B))
/// </code>
/// <para>
/// Parameters <c>A</c> (slope) and <c>B</c> (intercept) are fitted from labelled
/// verification samples using gradient descent.  After fitting, a raw score of
/// <c>0.80</c> from a well-calibrated model should produce a calibrated value of
/// approximately <c>0.80</c> (AC-1 requirement).
/// </para>
///
/// <para>This class contains no mutable state and is safe for concurrent use.</para>
/// </summary>
public static class PlattScaler
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Minimum number of verified samples required to fit reliable parameters.</summary>
    public const int MinimumSampleSize = 50;

    // Gradient-descent hyper-parameters (empirically reasonable for Platt scaling).
    private const int    MaxIterations  = 1_000;
    private const double LearningRate   = 0.01;
    private const double ConvergenceTol = 1e-6;

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transforms a raw AI confidence score into a calibrated probability using the
    /// Platt sigmoid: <c>P = 1 / (1 + exp(slope * rawScore + intercept))</c>.
    /// </summary>
    /// <param name="rawScore">Raw model output in [0, 1].</param>
    /// <param name="slope">Fitted slope parameter <c>A</c>.</param>
    /// <param name="intercept">Fitted intercept parameter <c>B</c>.</param>
    /// <returns>Calibrated probability in [0, 1].</returns>
    public static double CalibrateScore(double rawScore, double slope, double intercept)
    {
        return 1.0 / (1.0 + Math.Exp(slope * rawScore + intercept));
    }

    /// <summary>
    /// Fits Platt scaling parameters <c>(slope, intercept)</c> from labelled verification
    /// samples using binary cross-entropy gradient descent.
    /// </summary>
    /// <param name="samples">
    /// Collection of (predicted confidence, actual correct) pairs.
    /// <c>actualCorrect</c> is <c>true</c> when staff confirmed the AI result was correct.
    /// </param>
    /// <returns>
    /// The fitted <c>(slope, intercept)</c> tuple.
    /// Returns the identity-neutral defaults <c>(-1.0, 0.0)</c> when fewer than
    /// <see cref="MinimumSampleSize"/> samples are provided, which causes
    /// <see cref="CalibrateScore"/> to produce values near the raw score.
    /// </returns>
    public static (double slope, double intercept) FitParameters(
        IReadOnlyList<(double predicted, bool actualCorrect)> samples)
    {
        if (samples.Count < MinimumSampleSize)
        {
            // Identity-neutral: slope=-1, intercept=0 gives ~sigmoid(-score),
            // which approximates the raw score for typical mid-range inputs.
            // Callers should guard on MinimumSampleSize and use CalibrationPending instead.
            return (-1.0, 0.0);
        }

        double slope     = -1.0;   // Start near neutral
        double intercept =  0.0;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            double gradSlope     = 0.0;
            double gradIntercept = 0.0;

            foreach (var (pred, correct) in samples)
            {
                double p       = CalibrateScore(pred, slope, intercept);
                double target  = correct ? 1.0 : 0.0;
                double residual = p - target;

                gradSlope     += residual * pred;
                gradIntercept += residual;
            }

            gradSlope     /= samples.Count;
            gradIntercept /= samples.Count;

            double newSlope     = slope     - LearningRate * gradSlope;
            double newIntercept = intercept - LearningRate * gradIntercept;

            // Check convergence
            if (Math.Abs(newSlope - slope) < ConvergenceTol &&
                Math.Abs(newIntercept - intercept) < ConvergenceTol)
            {
                return (newSlope, newIntercept);
            }

            slope     = newSlope;
            intercept = newIntercept;
        }

        return (slope, intercept);
    }
}
