﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if UTILS_MATHNET_ENABLED
using MathNet.Numerics.LinearAlgebra;
#endif

namespace Latium
{
    public static class Logger
    {
        public delegate void WriteMethodDelegate(string text);

        public static WriteMethodDelegate WriteMethod;
        public static Stopwatch Stopwatch;
        public static string TimeStamp
        {
            get { return string.Format("[{0}]", Stopwatch.ElapsedMilliseconds); }
        }

        static Logger()
        {
            WriteMethod = (t) => Console.Write(t);
            Stopwatch = new Stopwatch();
            Stopwatch.Start();
        }

        public static void Log(object obj)
        {
            Log(obj.ToString());
        }

        public static void Log(string message)
        {
            WriteLine(string.Format("{0}[LOG] {1}", TimeStamp, message));
        }

        public static void Log(string tag, string message)
        {
            Log(tag + " - " + message);
        }

        public static void Log<T>(object sender, IEnumerable<T> data)
        {
            var b = new StringBuilder();
            b.Append("[ ");
            foreach(var item in data)
                b.Append($"{item.ToString()}, ");
            b.Append("]");
            Log(sender, b.ToString());
        }

        public static void Log(object sender, string message)
        {
            Log(sender.ToString() + " - " + message);
        }

        public static void Error(string message)
        {
            WriteLine(string.Format("{0}[ERR] {1}", TimeStamp, message));
        }

        public static void Error(object sender, string message)
        {
            Error(sender.ToString() + " - " + message);
        }

        public static void Error(object sender, Exception ex)
        {
            Error(sender, ex.ToString());
        }

        public static void Throw(string message)
        {
            WriteLine(string.Format("{0}[ERR] {1}", TimeStamp, message));
            throw new Exception(message);
        }

        public static void Throw(object sender, string message)
        {
            Throw(sender.ToString() + " - " + message);
        }

        public static void Throw(object sender, Exception ex)
        {
            Throw(sender, ex.ToString());
        }

#if UTILS_MATHNET_ENABLED

        public static string Print(Vector<double> vec)
        {
            return Print(vec.ToArray());
        }

        public static string Print(Matrix<double> mat)
        {
            return Print(mat.ToArray(), mat.RowCount, mat.ColumnCount);
        }
#endif

        public static string Print(double[] arry, string format = "0.0000")
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("{ ");
            foreach (double d in arry)
            {
                builder.Append($"{d.ToString(format)}, ");
            }
            builder.Append("}");

            return builder.ToString();
        }

        public static string Print(double[,] arry, double row, double col, string format = "0.0000")
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("{ ");
            for (int r = 0; r < row; r++)
            {
                builder.Append("{ ");
                for (int c = 0; c < col; c++)
                {
                    builder.Append($"{arry[r, c].ToString(format)}, ");
                }
                builder.Append("}");
            }
            builder.Append(" }");

            return builder.ToString();
        }

        private static void WriteLine(string str)
        {
            WriteMethod(str + "\n");
        }
    }
}