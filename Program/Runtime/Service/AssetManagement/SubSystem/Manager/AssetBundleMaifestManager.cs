﻿// zlib/libpng License
//
// Copyright (c) 2018 Sinoa
//
// This software is provided 'as-is', without any express or implied warranty.
// In no event will the authors be held liable for any damages arising from the use of this software.
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it freely,
// subject to the following restrictions:
//
// 1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software.
//    If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace IceMilkTea.Service
{
    /// <summary>
    /// アセットバンドルマニフェストを制御、管理を行うマネージャクラスです
    /// </summary>
    internal class AssetBundleManifestManager
    {
        // 定数定義
        private const string ManifestFileName = "AssetBundle.manifest";

        // メンバ変数定義
        private AssetBundleManifestFetcher fetcher;
        private ImtAssetBundleManifest manifest;
        private DirectoryInfo saveDirectoryInfo;



        /// <summary>
        /// AssetBundleManifestManager のインスタンスを初期化します
        /// </summary>
        /// <param name="fetcher">マニフェストの取り込みを行うフェッチャー</param>
        /// <param name="saveDirectoryInfo">マニフェストを保存するディレクトリ情報</param>
        /// <exception cref="ArgumentNullException">fetcher が null です</exception>
        /// <exception cref="ArgumentNullException">saveDirectoryInfo が null です</exception>
        public AssetBundleManifestManager(AssetBundleManifestFetcher fetcher, DirectoryInfo saveDirectoryInfo)
        {
            // もし null を渡された場合は
            if (fetcher == null)
            {
                // どうやってマニフェストを取り出そうか
                throw new ArgumentNullException(nameof(fetcher));
            }


            // 保存先ディレクトリ情報がnullなら
            if (saveDirectoryInfo == null)
            {
                // どこに保存すればいいんじゃ
                throw new ArgumentNullException(nameof(saveDirectoryInfo));
            }


            // 受け取る
            this.fetcher = fetcher;
            this.saveDirectoryInfo = saveDirectoryInfo;


            // マニフェストは空の状態で初期化
            manifest.LastUpdateTimeStamp = 0;
            manifest.ContentGroups = Array.Empty<AssetBundleContentGroup>();
        }


        #region Load and Save
        /// <summary>
        /// マニフェストファイルからマニフェストを非同期にロードします。
        /// あらゆる、操作の前に一度だけ実行するようにして下さい。
        /// </summary>
        /// <returns>非同期でロードしているタスクを返します</returns>
        public Task LoadManifestAsync()
        {
            // 保存ディレクトリ情報の更新とマニフェストファイルパスの用意
            saveDirectoryInfo.Refresh();
            var manifestFilePath = Path.Combine(saveDirectoryInfo.FullName, ManifestFileName);


            // マニフェストファイルが存在しないなら
            if (!File.Exists(manifestFilePath))
            {
                // ということはロードするものが無いので、完了タスクを返す
                return Task.CompletedTask;
            }


            // マニフェストのロード及びデシリアライズをタスクとして起動して返す
            return Task.Run(() =>
            {
                // マニフェストファイル内の文字列データをすべて読み込んで
                // Jsonデシリアライズしたものを自身の環境データとして初期化する
                var jsonData = File.ReadAllText(manifestFilePath);
                manifest = JsonUtility.FromJson<ImtAssetBundleManifest>(jsonData);
            });
        }


        /// <summary>
        /// 管理中のマニフェストをマニフェストファイルに非同期でセーブします。
        /// </summary>
        /// <returns>非同期でセーブしているタスクを返します</returns>
        private Task SaveManifestAsync()
        {
            // 保存ディレクトリ情報の更新を行ってディレクトリが存在していないなら
            saveDirectoryInfo.Refresh();
            if (!saveDirectoryInfo.Exists)
            {
                // ディレクトリを生成する
                saveDirectoryInfo.Create();
            }


            // マニフェストファイルパスの用意
            var manifestFilePath = Path.Combine(saveDirectoryInfo.FullName, ManifestFileName);


            // マニフェストのシリアリズ及びセーブをタスクとして起動して返す
            return Task.Run(() =>
            {
                // Jsonシリアライズしたものを、文字列データとしてマニフェストファイルに書き込む
                var jsonData = JsonUtility.ToJson(manifest);
                File.WriteAllText(manifestFilePath, jsonData);
            });
        }
        #endregion


        #region Manifest Fetch and Check and Update
        /// <summary>
        /// マニフェストの取り込みを非同期で行います。取り込んだマニフェストは、内部データに反映はされません。
        /// データとして更新が必要かどうかについては GetUpdatableAssetBundlesAsync() を用いてください。
        /// </summary>
        /// <returns>取り込みに成功した場合は、有効な参照の ImtAssetBundleManifest のインスタンスを返しますが、失敗した場合は null を返すタスクを返します</returns>
        public async Task<ImtAssetBundleManifest?> FetchManifestAsync()
        {
            // フェッチャーのフェッチをそのまま呼ぶ
            return await fetcher.FetchAsync();
        }


        /// <summary>
        /// 指定された新しいマニフェストを基に更新の必要のあるアセットバンドル情報の取得を非同期で行います。
        /// また、進捗通知はファイルチェック毎ではなく内部実装の既定に従った間隔で通知されます。
        /// </summary>
        /// <remarks>
        /// 最初の進捗通知が行われるよりも、先にチェックが完了した場合は一度も進捗通知がされないことに注意してください
        /// </remarks>
        /// <param name="newerManifest">新しいとされるマニフェスト</param>
        /// <param name="progress">チェック進捗通知を受ける Progress。もし通知を受けない場合は null の指定が可能です。</param>
        /// <returns>現在管理しているマニフェスト情報から、新しいマニフェスト情報で更新の必要なるアセットバンドル情報の配列を、操作しているタスクを返します。更新件数が 0 件でも長さ 0 の配列を返します</returns>
        /// <exception cref="ArgumentException">新しいマニフェストの '{nameof(ImtAssetBundleManifest.ContentGroups)}' が null です</exception>
        public Task<UpdatableAssetBundleInfo[]> GetUpdatableAssetBundlesAsync(ImtAssetBundleManifest newerManifest, IProgress<CheckAssetBundleProgress> progress)
        {
            // 渡されたアセットバンドルマニフェストが無効なカテゴリ配列を持っていた場合は
            if (newerManifest.ContentGroups == null)
            {
                // 引数の情報としてはあってはならないのでこれは例外とする
                throw new ArgumentException($"新しいマニフェストの '{nameof(ImtAssetBundleManifest.ContentGroups)}' が null です", nameof(newerManifest));
            }


            // もし 新しいマニフェストと言うなの古いマニフェスト または 新しいマニフェストのグループが0件なら
            if (manifest.LastUpdateTimeStamp >= newerManifest.LastUpdateTimeStamp || newerManifest.ContentGroups.Length == 0)
            {
                // 更新する必要性がないとして長さ0の結果のタスクを返す
                return Task.FromResult(Array.Empty<UpdatableAssetBundleInfo>());
            }


            // 現在のフレームレートを知るが未設定の場合は30FPSと想定し、通知間隔のミリ秒を求める（約2フレーム間隔とする）
            var currentTargetFrameRate = Application.targetFrameRate == -1 ? 30 : Application.targetFrameRate;
            var notifyIntervalTime = (int)(1.0 / currentTargetFrameRate * 2000.0);


            // マニフェストの更新チェックを行うタスクを生成して返す
            return Task.Run(() =>
            {
                // 進捗通知インターバル計測用ストップウォッチを起動
                var notifyIntervalStopwatch = Stopwatch.StartNew();


                // 古いコンテンツグループと新しいコンテンツグループの参照を拾う
                var olderContentGroups = manifest.ContentGroups;
                var newerContentGroups = newerManifest.ContentGroups;


                // 今のうちに、新しいグループ名リスト、継続グループ名リスト、削除グループ名リストを生成しておく
                var newGroupNameList = new List<string>();
                var removeGroupNameList = new List<string>();
                var continuationGroupNameList = new List<string>();


                // 新しいマニフェストのカテゴリ分回る
                for (int i = 0; i < newerContentGroups.Length; ++i)
                {
                    // もし同名の名前が見つかったのなら
                    var isNewGroupName = true;


                    // 古いマニフェストのカテゴリ分回る
                    for (int j = 0; j < olderContentGroups.Length; ++j)
                    {
                        if (newerContentGroups[i].Name == olderContentGroups[j].Name)
                        {
                            // 新しいグループ名ではないことを示してループから抜ける
                            isNewGroupName = false;
                            break;
                        }
                    }


                    // もし新しいグループ名なら
                    if (isNewGroupName)
                    {
                        // 新しいグループ名リストに追加
                        newGroupNameList.Add(newerContentGroups[i].Name);
                    }
                    else
                    {
                        // 継続グループ名リストに追加
                        continuationGroupNameList.Add(newerContentGroups[i].Name);
                    }
                }


                // 雑返却
                return Array.Empty<UpdatableAssetBundleInfo>();
            });
        }


        /// <summary>
        /// 指定された新しいマニフェストで、現在管理しているマニフェストに非同期で更新します。
        /// </summary>
        /// <param name="newerManifest">新しいとされるマニフェスト</param>
        /// <returns>マニフェストの更新を行っているタスクを返します</returns>
        public Task UpdateManifestAsync(ImtAssetBundleManifest newerManifest)
        {
            throw new NotImplementedException();
        }
        #endregion


        #region Get informations
        /// <summary>
        /// 指定された名前のアセットバンドル情報を取得します
        /// </summary>
        /// <param name="assetBundleName">アセットバンドル情報を取得する、アセットバンドル名</param>
        /// <param name="assetBundleInfo">取得されたアセットバンドルの情報を格納する参照</param>
        /// <exception cref="ArgumentNullException">assetBundleName が null です</exception>
        /// <exception cref="ArgumentException">アセットバンドル名 '{assetBundleName}' のアセットバンドル情報が見つかりませんでした</exception>
        public void GetAssetBundleInfo(string assetBundleName, out AssetBundleInfo assetBundleInfo)
        {
            // 名前に null が渡されたら
            if (assetBundleName == null)
            {
                // どうしろってんだい
                throw new ArgumentNullException(nameof(assetBundleName));
            }


            // 現在のマニフェストに含まれるコンテンツグループ分回る
            var contentGrops = manifest.ContentGroups;
            for (int i = 0; i < contentGrops.Length; ++i)
            {
                // コンテンツグループ内にあるアセットバンドル情報の数分回る
                var assetBundleInfos = contentGrops[i].AssetBundleInfos;
                for (int j = 0; j < assetBundleInfos.Length; ++j)
                {
                    // アセットバンドル名が一致したのなら
                    if (assetBundleInfos[j].Name == assetBundleName)
                    {
                        // この情報を渡して終了
                        assetBundleInfo = assetBundleInfos[j];
                        return;
                    }
                }
            }


            // ループから抜けてきたということは見つからなかったということ
            throw new ArgumentException($"アセットバンドル名 '{assetBundleName}' のアセットバンドル情報が見つかりませんでした", nameof(assetBundleName));
        }


        public string[] GetContentGroupNames()
        {
            throw new NotImplementedException();
        }


        public void GetContentGroupInfo(string contentGroupName, out AssetBundleContentGroup contentGroup)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}