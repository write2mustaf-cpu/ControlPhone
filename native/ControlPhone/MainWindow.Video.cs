using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ControlPhone.Core;

namespace ControlPhone;

/// <summary>
/// Cài đặt hình ảnh riêng cho MỘT máy: ô để trống = dùng cài đặt chung. Dùng khi một lưới có cả máy yếu và máy mạnh —
/// máy yếu nhận 480p/10 fps, máy mạnh nhận 1080p/30 fps, thay vì ép tất cả theo một mức.
/// </summary>
public partial class MainWindow
{
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
            meta.ThumbMaxSize = meta.ThumbFps = meta.HqMaxSize = meta.HqFps = meta.HqBitRate = null;
            Apply();
            return true;
        });
        dl.AddAction("Lưu", "Primary", () =>
        {
            var values = new[] { Read(thumbSize), Read(thumbFps), Read(hqSize), Read(hqFps), Read(hqRate) };
            if (values.Any(v => v == -1))
            {
                Ui.Toast(T("Giá trị không hợp lệ — hãy nhập số trong khoảng cho phép hoặc để trống"), "err", 6000);
                return false;
            }
            meta.ThumbMaxSize = values[0];
            meta.ThumbFps = values[1];
            meta.HqMaxSize = values[2];
            meta.HqFps = values[3];
            meta.HqBitRate = values[4] * 1000;
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
