using System;
using System.Collections.Concurrent;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 登录防爆破与账户锁定服务：防止暴力破解密码，连续失败达到阈值后自动锁定指定时长
/// </summary>
public class LoginAttemptLockoutService
{
    private sealed class AttemptRecord
    {
        public int FailedCount { get; set; }
        public DateTime FirstFailedAt { get; set; }
        public DateTime? LockedUntil { get; set; }
    }

    private readonly ConcurrentDictionary<string, AttemptRecord> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 检查指定用户名是否处于锁定状态
    /// </summary>
    public (bool IsLocked, TimeSpan RemainingTime) CheckLockout(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return (false, TimeSpan.Zero);

        if (_attempts.TryGetValue(userName, out var record))
        {
            var now = DateTime.UtcNow;
            if (record.LockedUntil.HasValue)
            {
                if (record.LockedUntil.Value > now)
                {
                    return (true, record.LockedUntil.Value - now);
                }
                else
                {
                    // 锁定时间已过，自动解除
                    _attempts.TryRemove(userName, out _);
                    return (false, TimeSpan.Zero);
                }
            }
        }

        return (false, TimeSpan.Zero);
    }

    /// <summary>
    /// 记录一次登录失败，若达到阈值则触发锁定
    /// </summary>
    public (bool IsNowLocked, int FailedCount, TimeSpan LockoutDuration) RecordFailedAttempt(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return (false, 0, TimeSpan.Zero);

        var now = DateTime.UtcNow;
        var record = _attempts.AddOrUpdate(
            userName,
            _ => new AttemptRecord { FailedCount = 1, FirstFailedAt = now },
            (_, existing) =>
            {
                if (now - existing.FirstFailedAt > WindowDuration)
                {
                    // 超出窗口重置
                    existing.FailedCount = 1;
                    existing.FirstFailedAt = now;
                    existing.LockedUntil = null;
                }
                else
                {
                    existing.FailedCount++;
                }

                if (existing.FailedCount >= MaxFailedAttempts)
                {
                    existing.LockedUntil = now.Add(LockoutDuration);
                }

                return existing;
            });

        bool isLocked = record.LockedUntil.HasValue && record.LockedUntil.Value > now;
        return (isLocked, record.FailedCount, LockoutDuration);
    }

    /// <summary>
    /// 登录成功后重置失败计数
    /// </summary>
    public void ResetAttempts(string userName)
    {
        if (!string.IsNullOrWhiteSpace(userName))
        {
            _attempts.TryRemove(userName, out _);
        }
    }
}

