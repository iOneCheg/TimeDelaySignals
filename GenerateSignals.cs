using DevExpress.XtraPrinting.Native;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace TimeDelaySignals
{
    public enum ModulationType
    {
        ASK,
        FSK,
        PSK
    }

    class GenerateSignals
    {
        /// <summary>
        /// Тип модуляции
        /// </summary>
        private ModulationType Type { get; }
        /// <summary>
        /// Количество бит
        /// </summary>
        public int BitsCount { get; }
        /// <summary>
        /// Скорость передачи сигнала
        /// </summary>
        public int BaudRate { get; }
        /// <summary>
        /// Длительность одного бита
        /// </summary>
        public double BitLength => 1d / BaudRate;
        /// <summary>
        /// Длительность опорного сигнала
        /// </summary>
        public double Tsample => BitsCount / (double)BaudRate;
        /// <summary>
        /// Длительность исследуемого сигнала
        /// </summary>
        public double TBigSample => (double)(BitsCountBigPSP * 2 + BitsCount) / BaudRate;
        /// <summary>
        /// Несущая частота
        /// </summary>
        public int CarrierFreq { get; }
        /// <summary>
        /// Частота дискретизации
        /// </summary>
        public int SamplingFreq { get; }
        /// <summary>
        /// Шаг между отсчетами
        /// </summary>
        public double dt => 1d / SamplingFreq;
        /// <summary>
        /// Задержка второго сигнала
        /// </summary>
        public int Delay { get; }  // в мс
        /// <summary>
        /// Количество целых бит для исследуемого сигнала
        /// </summary>
        public int BitsCountBigPSP =>
            (Delay % (int)(BitLength * 1000) != 0)
            ? (Delay / (int)(BitLength * 1000) + 1)
            : Delay / (int)(BitLength * 1000);
        /// <summary>
        /// Количество отсчетов на 1 бит
        /// </summary>
        public int TBit => (int)(TBigSample * SamplingFreq / (BitsCountBigPSP * 2 + BitsCount));
        /// <summary>
        /// Номер отсчета для отсечения "лишних" отсчетов не целого бита перед битом опорного сигнала
        /// </summary>
        private int CountStartCut => (int)(Delay / (dt * 1000));
        /// <summary>
        /// Номер отсчета начала опорного сигнала в исследуемом
        /// </summary>
        private int CountStart => BitsCountBigPSP * TBit;
        /// <summary>
        /// Номер отсчета конца опорного сигнала в исследуемом
        /// </summary>
        private int CountEnd => (BitsCountBigPSP + BitsCount) * TBit;
        /// <summary>
        ///  Номер отсчета для отсечения "лишних" отсчетов не целого бита после бита опорного сигнала
        /// </summary>
        private int CountEndCut => CountEnd + CountStartCut;
        /// <summary>
        /// Отсчеты исследумого сигнала
        /// </summary>
        public List<PointD> researchedSignal { get; private set; }
        /// <summary>
        /// Отсчеты опорного сигнала
        /// </summary>
        public List<PointD> desiredSignal { get; private set; }
        /// <summary>
        /// Отсчеты корреляции
        /// </summary>
        public List<PointD> correlation { get; private set; }

        public GenerateSignals(Tuple<ModulationType, int, int, int, int, int> paramSignal)
        {
            researchedSignal = new List<PointD>();
            desiredSignal = new List<PointD>();

            Type = paramSignal.Item1;
            BitsCount = paramSignal.Item2;
            BaudRate = paramSignal.Item3;
            CarrierFreq = paramSignal.Item4;
            SamplingFreq = paramSignal.Item5;
            Delay = paramSignal.Item6;
        }
        /// <summary>
        /// Генерация ПСП для исследуемого сигнала
        /// </summary>
        /// <param name="rnd"></param>
        /// <param name="CountBitsInDelay"></param>
        /// <param name="BitsCount"></param>
        /// <returns></returns>
        public static int[] BigPSPGenerate(Random rnd, int CountBitsInDelay, int BitsCount) =>
            Enumerable
            .Range(0, CountBitsInDelay * 2 + BitsCount)
            .Select(i => Convert.ToInt32(rnd.Next(2) == 0))
            .ToArray();

        public void ModulateSignals( Dictionary<string, object> modulateParam)
        {
            //Рассчет ПСП
            int[] Bigpsp = BigPSPGenerate(new Random(Guid.NewGuid().GetHashCode()),
              BitsCountBigPSP, BitsCount);

            int counter = 0, counterAll = 0;

            //Внешний цикл по длине ПСП
            for (int bit = 0; bit < Bigpsp.Length; bit++)
                //Цикл по каждому биту
                for (int i = 0; i < TBit; i++)
                {
                    //Условие для "отрезания" отсчетов не целых бит при не кратной задержке
                    if (!((counterAll > CountStartCut && counterAll < CountStart) || (counterAll > CountEndCut)))
                    {
                        double kAmplitude = (Bigpsp[bit] == 1d) ? (double)modulateParam["a1"] : (double)modulateParam["a0"],
                               kPhase = (Bigpsp[bit] == 1d) ? Math.PI : 0,
                               kFreq = (Bigpsp[bit] == 1d) ? (int)modulateParam["f1"] : (int)modulateParam["f0"];

                        double v = Type switch
                        {
                            ModulationType.ASK =>
                            kAmplitude * Math.Sin(2 * Math.PI * CarrierFreq * counter * dt),

                            ModulationType.PSK =>
                            Math.Sin(2 * Math.PI * CarrierFreq * counter * dt + Math.PI + kPhase),

                            ModulationType.FSK =>
                            Math.Sin(2 * Math.PI * kFreq * counter * dt),

                            _ => 0
                        };
                        researchedSignal.Add(new PointD(counter * dt, v));

                        //Условие для отсчетов опорного сигнала
                        if (counter >= CountStart && counter < CountEnd)
                            desiredSignal.Add(new PointD(counter * dt, v));

                        counter++;
                    }
                    counterAll++;
                }

        }
        /// <summary>
        /// Наложение шума на сигналы
        /// </summary>
        /// <param name="snrDb">SNR в дБ</param>
        public void MakeNoise(double snrDb)
        {
            // Наложение шума на искомый сигнал.
            desiredSignal = desiredSignal.Zip(
                    GenerateNoise(desiredSignal.Count, desiredSignal.Sum(p => p.Y * p.Y), 10),
                    (p, n) => new PointD(p.X, p.Y + n))
                .ToList();

            // Наложение шума на исследуемый сигнал.
            researchedSignal = researchedSignal.Zip(
                    GenerateNoise(researchedSignal.Count, researchedSignal.Sum(p => p.Y * p.Y), snrDb),
                    (p, n) => new PointD(p.X, p.Y + n))
                .ToList();
        }
        /// <summary>
        /// Рассчет корреляции
        /// </summary>
        /// <param name="maxIndex">Индекс max корреляции</param>
        public void CalculateCorrelation(out int maxIndex)
        {
            var result = new List<PointD>();
            var maxCorr = double.MinValue;
            var index = 0;
            for (var i = 0; i < researchedSignal.Count - desiredSignal.Count + 1; i++)
            {
                var corr = 0d;
                for (var j = 0; j < desiredSignal.Count; j++)
                    corr += researchedSignal[i + j].Y * desiredSignal[j].Y;
                result.Add(new PointD(researchedSignal[i].X, corr / desiredSignal.Count));

                if (result[i].Y > maxCorr)
                {
                    maxCorr = result[i].Y;
                    index = i;
                }
            }

            maxIndex = index;
            correlation = result;
        }
        /// <summary>
        /// Генерация случайного числа по нормальному распределению
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        private static double GetNormalRandom(double min, double max, int n = 12)
        {
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            var sum = 0d;
            for (var i = 0; i < n; i++)
                sum += rnd.NextDouble() * (max - min) + min;
            return sum / n;
        }
        /// <summary>
        /// Генерация белого гауссовского шума
        /// </summary>
        /// <param name="countNumbers">Число отсчетов</param>
        /// <param name="energySignal">Энергия сигнала</param>
        /// <param name="snrDb">SNR в дБ</param>
        /// <returns></returns>
        private static IEnumerable<double> GenerateNoise(int countNumbers, double energySignal, double snrDb)
        {
            var noise = new List<double>();
            for (var i = 0; i < countNumbers; i++)
                noise.Add(GetNormalRandom(-1d, 1d));

            // Нормировка шума.
            var snr = Math.Pow(10, -snrDb / 10);
            var norm = Math.Sqrt(snr * energySignal / noise.Sum(y => y * y));

            return noise.Select(y => y * norm).ToList();
        }
        public void CrossCorrelate(out int Max)
        {

            int N = Math.Max(researchedSignal.Count, desiredSignal.Count);
            Complex32[] xComplex = new Complex32[N];
            Complex32[] yComplex = new Complex32[N];

            for (int i = 0; i < researchedSignal.Count; i++)
            {
                xComplex[i] = new Complex32((float)researchedSignal[i].Y, 0);
            }

            for (int i = 0; i < desiredSignal.Count; i++)
            {
                yComplex[i] = new Complex32((float)desiredSignal[i].Y, 0);
            }

            Fourier.Forward(xComplex);
            Fourier.Forward(yComplex);

            Complex[] product = new Complex[N];
            for (int i = 0; i < N; i++)
            {
                product[i] = xComplex[i].ToComplex() * Complex.Conjugate(yComplex[i].ToComplex());
            }

            Fourier.Inverse(product);

            List<PointD> result = new List<PointD>();
            for (int i = 0; i < N; i++)
            {
                result.Add(new PointD(researchedSignal[i].X, product[i].Real));
            }
            double t = result.Max(p => p.Y);
            Max = 0;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Y == t) Max = i;

            }
            correlation = result;

        }

    }
}
