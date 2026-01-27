using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGemCo2DCore;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 몬스터 BT Tree 에셋을 로드하고 <see cref="MonsterBtRunner"/>에 적용한다.
    /// </summary>
    /// <remarks>
    /// 실무 포인트 반영:
    /// - 예외 처리: 실패는 로그만 남기고 다음 단계로 진행.
    /// - 중복 로드 방지: Addressables 핸들 캐시 재사용.
    /// - 핸들 해제: 맵 언로드 시 <see cref="ReleaseAll"/> 호출.
    /// </remarks>
    internal static class AddressableLoaderMonsterBt
    {
        private static readonly Dictionary<string, AsyncOperationHandle<MonsterBehaviorTreeAsset>> HandleCache
            = new(StringComparer.Ordinal);

        /// <summary>
        /// 몬스터 오브젝트에 연결된 BT 에셋을 로드/적용한다.
        /// </summary>
        public static async Task LoadAndApplyAsync(string key, MonsterBtRunner runner)
        {
            if (string.IsNullOrEmpty(key)) return;

            // Addressables 로드
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                var asset = await LoadByKeyAsync(key);
                if (asset != null)
                    runner.SetTree(asset);
            }
            catch (Exception e)
            {
                // 정책: 실패는 로그만 남기고 진행
                GcLogger.LogException(e);
            }
        }

        private static async Task<MonsterBehaviorTreeAsset> LoadByKeyAsync(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (HandleCache.TryGetValue(key, out var cached))
            {
                // 이미 완료된 경우 즉시 반환
                if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
                    return cached.Result;

                // 진행 중/실패한 경우 Task 대기 후 재평가
                try { await cached.Task; } catch { /* 아래에서 상태 검사 */ }

                if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
                    return cached.Result;

                // 실패 상태면 캐시 제거 후 재시도
                HandleCache.Remove(key);
            }

            AsyncOperationHandle<MonsterBehaviorTreeAsset> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<MonsterBehaviorTreeAsset>(key);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BT] Addressables.LoadAssetAsync failed. key={key}\n{e}");
                return null;
            }

            HandleCache[key] = handle;

            try
            {
                await handle.Task;
            }
            catch
            {
                // handle.Status로 판정
            }

            if (!handle.IsValid())
                return null;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[BT] Tree asset load failed. key={key} status={handle.Status}");
                return null;
            }

            return handle.Result;
        }

        /// <summary>
        /// 캐시된 Addressables 핸들을 모두 해제한다.
        /// </summary>
        public static void ReleaseAll()
        {
            foreach (var kv in HandleCache)
            {
                var handle = kv.Value;
                if (handle.IsValid())
                {
                    try { Addressables.Release(handle); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            HandleCache.Clear();
        }
    }
}
