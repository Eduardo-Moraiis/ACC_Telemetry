using System;

namespace AccTelemetry.Domain;

public static class TrackMapGenerator
{
    public static (double[] X, double[] Y) Generate2D(double[] speedKmh, double[] yawRateRadSec, double[] timeSec)
    {
        int count = speedKmh.Length;
        if (count < 2 || yawRateRadSec.Length != count) return ([], []);

        // 1. Calcula a rotação total angular na volta
        double totalAngularTurn = 0.0;
        for (int i = 1; i < count; i++)
        {
            double dt = timeSec[i] - timeSec[i - 1];
            totalAngularTurn += yawRateRadSec[i] * dt;
        }

        // Circuitos no sentido horário giram exatos -360° (-2*PI)
        double targetTurn = totalAngularTurn < 0 ? -2.0 * Math.PI : 2.0 * Math.PI;

        // Remove o Drift do Giroscópio
        double totalLapTime = timeSec[^1] - timeSec[0];
        double gyroDriftPerSec = (totalAngularTurn - targetTurn) / totalLapTime;

        var rawX = new double[count];
        var rawY = new double[count];
        double heading = 0.0;

        for (int i = 1; i < count; i++)
        {
            double dt = timeSec[i] - timeSec[i - 1];
            double speedMs = speedKmh[i] / 3.6;
            double correctedYaw = yawRateRadSec[i] - gyroDriftPerSec;

            heading -= correctedYaw * dt; // Inverte para plano 2D padrão

            rawX[i] = rawX[i - 1] + (speedMs * Math.Cos(heading) * dt);
            rawY[i] = rawY[i - 1] + (speedMs * Math.Sin(heading) * dt);
        }

        // 2. Fechamento de Posição (Garante que a volta se fecha perfeitamente)
        double deltaX = rawX[^1] - rawX[0];
        double deltaY = rawY[^1] - rawY[0];

        var finalX = new double[count];
        var finalY = new double[count];

        for (int i = 0; i < count; i++)
        {
            double fraction = (double)i / (count - 1);
            finalX[i] = rawX[i] - (deltaX * fraction);
            finalY[i] = rawY[i] - (deltaY * fraction);
        }

        return (finalX, finalY);
    }
}