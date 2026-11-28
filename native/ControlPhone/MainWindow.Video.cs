using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ControlPhone.Core;

namespace ControlPhone;

/// <summary>
/// Cài đặt hình ảnh riêng cho MỘT máy: ô để trống = dùng cài đặt chung. Dùng khi một lưới có cả máy yếu và máy mạnh —
/// máy yếu nhận 480p/10 fps, máy mạnh nhận 1080p/30 fps, thay vì ép tất cả theo một mức.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Vòng điều chỉnh chất lượng: mỗi 2 giây đọc số đo thật của từng luồng lưới. Khi bộ điều chỉnh hạ mức của một máy, luồng
    /// của riêng máy đó được mở lại (các máy khác không bị ảnh hưởng); khi nâng lại mức thì chờ lần mở luồng kế tiếp.
    /// </summary>
    void InitAutoQuality()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            if (!Store.Settings.AutoQuality) return;
            var now = Environment.TickCount64;
            var cpu = AutoQuality.CpuLoad();
            var all = manager.Devices.ToList();
            var streams = all.Count(x => x.ThumbDecoder != null);
            LoadBudget.SetActive(streams, Environment.ProcessorCount);
            NotifyBudget(streams);
            foreach (var d in all)
            {
                var decision = AutoQuality.Observe(d, now, cpu, streams);
                if (decision is { Changed: true } && d.OpenedPlan is { } opened && d.HqRefs == 0
                    && d.ActiveAddr != null && !d.Pending && d.ThumbDecoder != null)
                {
                    var t = decision.Target;
                    if (t.MaxFps < opened.MaxFps || t.BitRate < opened.BitRate || t.MaxSize < opened.MaxSize)
                    {
                        manager.RestartSessions(d);
                        Ui.Toast(T("Máy #{0}: hạ chất lượng lưới xuống {1}p/{2} fps — {3}", d.Num, t.MaxSize, t.MaxFps, decision.Reason), "", 4000);
                    }
                }
                if (Store.Settings.ShowPerf) d.RaisePerf();
            }
        };
        timer.Start();
    }

    string budgetNote = "";

    /// <summary>Khi ngân sách chia lại làm lưới nhẹ đi, nói một lần cho người dùng biết (không im lặng hạ chất lượng).</summary>
    void NotifyBudget(int streams)
    {
        if (!Store.Settings.LoadBudget || LoadBudget.ActiveShare <= 0 || streams < 4) { budgetNote = ""; return; }
        var s = Store.Settings;
        var plan = LoadBudget.Fit(DeviceVideo.ThumbBase(null, s), LoadBudget.ActiveShare);
        var note = $"{streams}|{plan.MaxSize}|{plan.MaxFps}|{plan.BitRate}";
        if (note == budgetNote) return;
        var first = budgetNote == "";
        budgetNote = note;
        if (!first) return;                     // chỉ nhắc khi tình trạng đổi hẳn, không nhắc lại mỗi 2 giây
        Ui.Toast(T("Chia ngân sách giải mã cho {0} màn hình: mỗi màn hình {1}p · {2} fps · {3} kbps", streams, plan.MaxSize, plan.MaxFps, plan.BitRate / 1000), "", 6000);
    }

    void DlgDeviceVideo(PhoneDevice d)
    {
        var meta = Store.Meta(d.Id);
        var s = Store.Settings;
        var dl = new Dlg(T("Cài đặt hình ảnh cho máy #{0}", d.Num), 520);
        dl.AddText("Ô nào để trống thì dùng cài đặt chung. Áp dụng ngay sau khi lưu: luồng của máy này được mở lại.");
        dl.AddNote(T("Cài đặt chung hiện tại — lưới: {0}p · {1} fps · {2} kbps; xem lớn: {3}p · {4} fps · {5} kbps",
            s.ThumbMaxSize, s.ThumbFps, s.ThumbBitRate / 1000, s.HqMaxSize, s.HqFps, s.HqBitRate / 1000));

        TextBox Num(string label, int? value, int min, int max, string placeholder)
        {
            var tb = dl.AddInput(label, value?.ToString(CultureInfo.InvariantCulture) ?? "", placeholder);
            tb.Tag = (min, max);
            return tb;
        }

        var thumbSize = Num("Lưới — độ phân giải tối đa (px)", meta.ThumbMaxSize, 160, 1920, "trống = " + s.ThumbMaxSize);
        var thumbFps = Num("Lưới — số khung hình/giây", meta.ThumbFps, 1, 60, "trống = " + s.ThumbFps);
        var thumbRate = Num("Lưới — bitrate (kbps)", meta.ThumbBitRate is int tr ? tr / 1000 : null, 30, 20_000, "trống = " + s.ThumbBitRate / 1000);
        var hqSize = Num("Xem lớn — độ phân giải tối đa (px)", meta.HqMaxSize, 320, 2560, "trống = " + s.HqMaxSize);
        var hqFps = Num("Xem lớn — số khung hình/giây", meta.HqFps, 5, 60, "trống = " + s.HqFps);
        var hqRate = Num("Xem lớn — bitrate (kbps)", meta.HqBitRate is int r ? r / 1000 : null, 500, 40_000, "trống = " + s.HqBitRate / 1000);

        // trả về: null = để trống (dùng chung), -1 = giá trị sai, >0 = giá trị hợp lệ
        int? Read(TextBox tb)
        {
            var text = tb.Text.Trim();
            if (text == "") return null;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return -1;
            var (min, max) = ((int, int))tb.Tag;
            return n < min || n > max ? -1 : n;
        }

        dl.AddAction("Đóng");
        dl.AddAction("Dùng cài đặt chung", "Btn", () =>
        {
            meta.ThumbMaxSize = meta.ThumbFps = meta.ThumbBitRate = meta.HqMaxSize = meta.HqFps = meta.HqBitRate = null;
            meta.AutoMaxSize = meta.AutoFps = meta.AutoBitRate = null;
            Apply();
            return true;
        });
        dl.AddAction("Lưu", "Primary", () =>
        {
            var values = new[] { Read(thumbSize), Read(thumbFps), Read(thumbRate), Read(hqSize), Read(hqFps), Read(hqRate) };
            if (values.Any(v => v == -1))
            {
                Ui.Toast(T("Giá trị không hợp lệ — hãy nhập số trong khoảng cho phép hoặc để trống"), "err", 6000);
                return false;
            }
            meta.ThumbMaxSize = values[0];
            meta.ThumbFps = values[1];
            meta.ThumbBitRate = values[2] * 1000;
            meta.HqMaxSize = values[3];
            meta.HqFps = values[4];
            meta.HqBitRate = values[5] * 1000;
            Apply();
            return true;
        });

        void Apply()
        {
            Store.Save();
            manager.RestartSessions(d);
            var plan = DeviceVideo.Thumb(meta, s);
            var hq = DeviceVideo.Hq(meta, s);
            Ui.Toast(T("Đã áp dụng cho máy #{0}: lưới {1}p/{2} fps · xem lớn {3}p/{4} fps", d.Num, plan.MaxSize, plan.MaxFps, hq.MaxSize, hq.MaxFps), "ok", 5000);
            d.Changed();
            RenderAll();
        }

        dl.ShowDialog();
    }
}
