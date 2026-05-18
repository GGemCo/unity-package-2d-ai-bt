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
    /// 몬스터 BT 에셋을 Addressables에서 로드/캐시하는 유틸리티입니다.
    /// </summary>
    internal static class AddressableLoaderMonsterBt
    {
        private static readonly Dictionary<string, AsyncOperationHandle<MonsterBehaviorTreeAsset>> HandleCache
            = new Dictionary<string, AsyncOperationHandle<MonsterBehaviorTreeAsset>>(StringComparer.Ordinal);

        /// <summary>
        /// BT 키를 로드하여 지정된 러너에 즉시 적용합니다.
        /// </summary>
        /// <param name="key">Addressables 키입니다.</param>
        /// <param name="runner">적용 대상 러너입니다.</param>
        public static async Task LoadAndApplyAsync(string key, MonsterBtRunner runner)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                MonsterBehaviorTreeAsset asset = await LoadTreeAssetAsync(key);
                if (asset != null)
                    runner.SetTree(asset);
            }
            catch (Exception e)
            {
                // 정책: 로드 실패 시 예외를 외부로 던지지 않고 로그만 남깁니다.
                GcLogger.LogException(e);
            }
        }

        /// <summary>
        /// BT Addressables 키로 에셋을 로드합니다.
        /// </summary>
        /// <param name="key">Addressables 키입니다.</param>
        /// <returns>로드된 BT 에셋입니다. 실패 시 null을 반환합니다.</returns>
        public static Task<MonsterBehaviorTreeAsset> LoadTreeAssetAsync(string key)
        {
            return LoadByKeyAsync(key);
        }

        /// <summary>
        /// BT 에셋을 캐시를 포함해 로드합니다.
        /// </summary>
        /// <param name="key">Addressables 키입니다.</param>
        /// <returns>로드된 BT 에셋입니다. 실패 시 null을 반환합니다.</returns>
        private static async Task<MonsterBehaviorTreeAsset> LoadByKeyAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (HandleCache.TryGetValue(key, out AsyncOperationHandle<MonsterBehaviorTreeAsset> cached))
            {
                // 이미 성공한 핸들이 있으면 즉시 재사용합니다.
                if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
                    return cached.Result;

                // 진행 중/실패 상태를 한 번 더 관측한 뒤 상태를 재평가합니다.
                try
                {
                    await cached.Task;
                }
                catch
                {
                    // 상태 판정은 아래 cached.Status로 처리합니다.
                }

                if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
                    return cached.Result;

                // 실패한 캐시는 제거 후 재시도합니다.
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
                // handle.Status로 최종 성공 여부를 판단합니다.
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
        /// 캐시된 Addressables 핸들을 모두 해제합니다.
        /// </summary>
        public static void ReleaseAll()
        {
            foreach (KeyValuePair<string, AsyncOperationHandle<MonsterBehaviorTreeAsset>> kv in HandleCache)
            {
                AsyncOperationHandle<MonsterBehaviorTreeAsset> handle = kv.Value;
                if (!handle.IsValid())
                    continue;

                try
                {
                    Addressables.Release(handle);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            HandleCache.Clear();
        }
    }
}
