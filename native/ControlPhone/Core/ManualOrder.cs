namespace ControlPhone.Core;

/// <summary>
/// Thứ tự ô trên lưới do người dùng tự xếp. Danh sách lưu id máy; máy mới thêm vào cuối, máy đã gỡ bị bỏ khỏi danh sách,
/// nên danh sách cũ không làm mất hay nhân đôi ô nào.
/// </summary>
public static class ManualOrder
{
    /// <summary>Áp thứ tự đã lưu lên danh sách máy đang có, giữ nguyên các máy chưa từng được xếp (thêm vào cuối).</summary>
    public static List<string> Normalize(IEnumerable<string> present, IReadOnlyList<string> stored)
    {
        var ids = present.ToList();
        var here = ids.ToHashSet();
        var res = new List<string>(ids.Count);
        var used = new HashSet<string>();
        foreach (var id in stored) if (here.Contains(id) && used.Add(id)) res.Add(id);
        foreach (var id in ids) if (used.Add(id)) res.Add(id);
        return res;
    }

    /// <summary>Dời một máy <paramref name="delta"/> vị trí (âm = về trước); dừng ở hai đầu thay vì tràn ra ngoài.</summary>
    public static List<string> Move(IReadOnlyList<string> order, string id, int delta)
    {
        var list = order.ToList();
        int i = list.IndexOf(id);
        if (i < 0 || delta == 0) return list;
        int j = Math.Clamp(i + delta, 0, list.Count - 1);
        if (i == j) return list;
        list.RemoveAt(i);
        list.Insert(j, id);
        return list;
    }

    /// <summary>
    /// Dời máy <paramref name="id"/> qua một máy khác trong danh sách đầy đủ: chèn ngay trước (delta &lt; 0) hoặc ngay sau
    /// (delta &gt; 0) máy lân cận. Nhờ vậy lưới đang lọc vẫn dời đúng chỗ so với các máy bị ẩn.
    /// </summary>
    public static List<string> MovePast(IReadOnlyList<string> full, string id, string neighbour, int delta)
    {
        var list = full.ToList();
        if (!list.Remove(id)) return list;
        int at = list.IndexOf(neighbour);
        if (at < 0) return list;
        list.Insert(delta < 0 ? at : at + 1, id);
        return list;
    }
}
