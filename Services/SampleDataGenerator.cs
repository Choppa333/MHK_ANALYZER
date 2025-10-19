using MGK_Analyzer.Models;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MGK_Analyzer.Services
{
    /// <summary>
    /// 3D 차트용 샘플 데이터를 생성하는 클래스입니다.
    /// </summary>
    public static class SampleDataGenerator
    {
        /// <summary>
        /// 3D Surface 및 Contour 차트를 위한 샘플 데이터를 생성합니다.
        /// 이 데이터는 제공된 CSV 값을 사용합니다. (Speed, Torque, Motor_구동효율)
        /// X축: Speed (속도)
        /// Y축: Torque (토크)
        /// Z축: Motor_구동효율 (모터 구동 효율)
        /// </summary>
        /// <returns>생성된 3D 데이터 포인트 리스트</returns>
        public static List<Surface3DPoint> CreateSample3DData()
        {
            var data = new List<Surface3DPoint>();
            
            // CSV 형식 데이터: Speed, Torque, Motor_구동효율
            var csvData = @"250.54850746268656,25.091305970149236,81.52555970149254
250.0952380952381,50.800119047619113,83.939523809523806
250.97647058823529,101.6081176470588,79.354470588235301
250.9,151.84075000000001,78.233500000000021
252.64227642276424,202.45772357723587,75.167967479674886
252.48571428571429,252.59171428571432,71.581047619047638
252.19565217391303,303.00956521739118,67.087898550724702
252.82692307692307,353.62942307692276,61.908269230769193
250.4047619047619,398.80976190476167,58.800714285714307
500.79695431472084,24.834416243654818,89.181015228426418
500.56204379562041,50.468321167883239,90.390802919708079
500.66956521739132,101.11347826086956,89.038086956521639
501.41935483870969,151.44225806451618,87.084435483870976
502.26119402985074,202.12141791044755,85.312388059701561
501.35779816513764,251.76146788990835,82.903853211009235
500.61165048543688,302.45631067961159,80.511359223301028
501.00943396226415,352.68726415094397,77.756509433962293
1000.1511627906976,24.545290697674396,92.307034883721016
999.82905982905982,50.163333333333377,93.578034188034195
1000.3451327433628,100.66486725663718,93.284955752212312
1001,150.94256756756769,92.308783783783795
1000.9253731343283,201.3928358208955,91.2911940298508
1000.8125,251.21302083333356,90.060416666666683
1000.4166666666666,301.81535714285712,88.642857142857125
1000.8245614035088,351.62350877192989,86.974912280701744
1000.8389830508474,397.07127118644036,85.34305084745759
1499.4952380952382,24.435142857142836,93.370666666666651
1498.9620253164558,50.035569620253185,94.710126582278548
1499.6617647058824,100.57073529411767,94.798088235294117
1500.141791044776,150.70119402985074,94.177462686567083
1500.5677966101696,200.96627118644093,93.460762711864334
1499.780701754386,251.00078947368399,92.668947368421072
1500.8466257668711,301.48601226993839,91.728343558282219
1500.7207207207207,351.14099099099099,90.516756756756834
1500.25,396.24629999999991,89.279899999999927";

            using (var reader = new StringReader(csvData))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var values = line.Split(',');
                    if (values.Length == 3)
                    {
                        try
                        {
                            // X = Speed (속도), Y = Torque (토크), Z = Motor_구동효율
                            double speed = double.Parse(values[0], CultureInfo.InvariantCulture);
                            double torque = double.Parse(values[1], CultureInfo.InvariantCulture);
                            double efficiency = double.Parse(values[2], CultureInfo.InvariantCulture);
                            
                            data.Add(new Surface3DPoint(speed, torque, efficiency));
                        }
                        catch (FormatException)
                        {
                            // 파싱 오류가 있는 줄은 건너뜁니다.
                        }
                    }
                }
            }
            
            return data;
        }

        /// <summary>
        /// 지정된 Excel 파일에서 3D 데이터를 읽어옵니다.
        /// </summary>
        /// <param name="filePath">Excel 파일 경로</param>
        /// <returns>읽어온 3D 데이터 포인트 리스트</returns>
        public static List<Surface3DPoint> ReadFromExcel(string filePath)
        {
            var data = new List<Surface3DPoint>();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // 'T-N' 시트를 선택합니다.
                var worksheet = package.Workbook.Worksheets["T-N"];
                if (worksheet == null)
                {
                    throw new Exception("'T-N' 시트를 찾을 수 없습니다.");
                }

                // M, N, R 열의 인덱스를 찾습니다. (M=13, N=14, R=18)
                const int colM = 13;
                const int colN = 14;
                const int colR = 18;

                // 데이터는 3행부터 시작한다고 가정합니다.
                for (int row = 3; row <= worksheet.Dimension.End.Row; row++)
                {
                    try
                    {
                        // 각 셀에서 값을 double로 변환합니다.
                        var x_val = worksheet.Cells[row, colM].Value;
                        var y_val = worksheet.Cells[row, colN].Value;
                        var z_val = worksheet.Cells[row, colR].Value;

                        if (x_val != null && y_val != null && z_val != null)
                        {
                            double x = Convert.ToDouble(x_val);
                            double y = Convert.ToDouble(y_val);
                            double z = Convert.ToDouble(z_val);

                            data.Add(new Surface3DPoint(x, y, z));
                        }
                    }
                    catch (Exception)
                    {
                        // 특정 행에서 오류가 발생하더라도 계속 진행합니다.
                        // Console.WriteLine($"Row {row} parsing error: {ex.Message}");
                    }
                }
            }

            return data;
        }
    }
}

