using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class RemoteConfigLoader : MonoBehaviour
{
    [Header("Remote config")]
    [Tooltip("URL CSV или JSON с конфигом")]
    public string configUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vRoDqEnm7UilQbJxnYkgBHKI7MqvwVbUf1Ey89k1HLAPxemuAAm3hu2_nWOXauTRwIqs43x7XE88qCe/pub?output=csv";

    [Tooltip("Формат источника: CSV или JSON")]
    public ConfigFormat format = ConfigFormat.CSV;

    public enum ConfigFormat { CSV, JSON }

    // Локальная копия
    private string LocalPath =>
        Path.Combine(Application.persistentDataPath, "weapons_cache." + (format == ConfigFormat.CSV ? "csv" : "json"));

    // Заранее прописанные дефолтные значения
    private readonly List<Weapon> _defaults = new List<Weapon>
    {
        new Weapon("default_pistol", 10f, 0.5f),
        new Weapon("default_rifle",  20f, 1.0f),
    };

    public IReadOnlyList<Weapon> Loaded { get; private set; } = new List<Weapon>();

    private void Start()
    {
        StartCoroutine(LoadRoutine(result => Debug.Log($"[RemoteConfigLoader] Загружено оружий: {result.Count}")));
    }

    public IEnumerator LoadRoutine(Action<List<Weapon>> onDone)
    {
        string raw = null;

        // 1. Пытаемся загрузить по URL
        using (var req = UnityWebRequest.Get(configUrl))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (ok)
            {
                raw = req.downloadHandler.text;
                // 4. Сохраняем локальную копию
                try
                {
                    File.WriteAllText(LocalPath, raw, Encoding.UTF8);
                    Debug.Log($"[RemoteConfigLoader] Кэш сохранён: {LocalPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteConfigLoader] Не удалось сохранить кэш: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[RemoteConfigLoader] Ошибка загрузки: {req.error}");
            }
        }

        // 2. Фолбэк на локальную копию
        if (string.IsNullOrEmpty(raw) && File.Exists(LocalPath))
        {
            try
            {
                raw = File.ReadAllText(LocalPath, Encoding.UTF8);
                Debug.Log("[RemoteConfigLoader] Используем локальную копию.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfigLoader] Не удалось прочитать кэш: {e.Message}");
            }
        }

        // 3. Фолбэк на дефолты
        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogError("[RemoteConfigLoader] Сеть и кэш недоступны — используем дефолтные значения.");
            Loaded = _defaults;
            onDone?.Invoke(new List<Weapon>(_defaults));
            yield break;
        }

        // Парсинг + валидация
        List<Weapon> parsed = format == ConfigFormat.CSV ? ParseCsv(raw) : ParseJson(raw);

        if (parsed == null || parsed.Count == 0)
        {
            Debug.LogError("[RemoteConfigLoader] Парсинг не удался — используем дефолты.");
            parsed = new List<Weapon>(_defaults);
        }

        Loaded = parsed;
        onDone?.Invoke(parsed);
    }

    // ---------- Парсеры ----------

    private List<Weapon> ParseCsv(string text)
    {
        var list = new List<Weapon>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        if (lines.Length < 2) return list;

        // Пропускаем заголовок; предполагаем порядок id, damage, cooldown
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = SplitCsvLine(line);
            if (cols.Count < 3)
            {
                Debug.LogWarning($"[RemoteConfigLoader] Пропущена строка {i}: недостаточно колонок");
                continue;
            }

            if (!TryBuildWeapon(cols[0], cols[1], cols[2], out var w, i)) continue;
            list.Add(w);
        }
        return list;
    }

    private static List<string> SplitCsvLine(string line)
    {
        // Простой CSV-парсер с поддержкой кавычек
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    private List<Weapon> ParseJson(string text)
    {
        var list = new List<Weapon>();
        WeaponListDto dto = null;
        try
        {
            // Пробуем сначала как {"items":[...]}
            dto = JsonUtility.FromJson<WeaponListDto>(text);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RemoteConfigLoader] JSON (items) ошибка: {e.Message}");
        }

        if (dto?.items == null)
        {
            // Пробуем как массив — оборачиваем
            try
            {
                dto = JsonUtility.FromJson<WeaponListDto>("{\"items\":" + text + "}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteConfigLoader] JSON ошибка: {e.Message}");
                return list;
            }
        }

        if (dto?.items == null) return list;

        for (int i = 0; i < dto.items.Length; i++)
        {
            var it = dto.items[i];
            if (it == null) continue;
            if (!TryBuildWeapon(it.id, it.damage.ToString(), it.cooldown.ToString(), out var w, i)) continue;
            list.Add(w);
        }
        return list;
    }

    // ---------- Валидация ----------

    private bool TryBuildWeapon(string idRaw, string dmgRaw, string cdRaw, out Weapon weapon, int index)
    {
        weapon = null;
        var id = idRaw?.Trim().Trim('"');
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning($"[RemoteConfigLoader] Строка {index}: пустой id — пропуск.");
            return false;
        }

        if (!float.TryParse(dmgRaw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var damage))
        {
            Debug.LogWarning($"[RemoteConfigLoader] Строка {index} ({id}): damage не число — пропуск.");
            return false;
        }

        if (!float.TryParse(cdRaw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var cooldown))
        {
            Debug.LogWarning($"[RemoteConfigLoader] Строка {index} ({id}): cooldown не число — пропуск.");
            return false;
        }

        if (damage < 0f)
        {
            Debug.LogWarning($"[RemoteConfigLoader] {id}: damage={damage} < 0 — пропуск.");
            return false;
        }

        if (cooldown <= 0f)
        {
            Debug.LogWarning($"[RemoteConfigLoader] {id}: cooldown={cooldown} <= 0 — пропуск.");
            return false;
        }

        weapon = new Weapon(id, damage, cooldown);
        return true;
    }
}