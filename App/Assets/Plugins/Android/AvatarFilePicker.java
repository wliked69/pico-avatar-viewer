package com.psp.avatarviewer;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import com.unity3d.player.UnityPlayer;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.UUID;

// SAF grants access to the selected document; no broad storage permission is needed.
public final class AvatarFilePicker extends Fragment {
    private boolean launched;
    public static void open(String receiver, String method) {
        Activity activity=UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            AvatarFilePicker picker=new AvatarFilePicker();
            Bundle args=new Bundle(); args.putString("receiver",receiver); args.putString("method",method); picker.setArguments(args);
            activity.getFragmentManager().beginTransaction().add(picker,"avatar-file-picker").commit();
        });
    }
    @Override public void onStart() {
        super.onStart(); if(launched) return; launched=true;
        Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT); intent.setType("*/*"); intent.addCategory(Intent.CATEGORY_OPENABLE);
        try { startActivityForResult(intent,7021); }
        catch(Exception error) { reply("ERROR:没有可用的系统文件选择器"); finish(); }
    }
    @Override public void onActivityResult(int code,int result,Intent data) {
        if(code!=7021) return;
        if(result!=Activity.RESULT_OK || data==null || data.getData()==null) { reply("ERROR:已取消导入"); finish(); return; }
        Uri uri=data.getData(); final Activity activity=getActivity();
        new Thread(() -> {
            File output=null;
            try {
                File folder=new File(activity.getExternalFilesDir(null),"Models"); if(!folder.exists() && !folder.mkdirs()) throw new Exception("无法创建模型库");
                String display="Imported";
                try(android.database.Cursor cursor=activity.getContentResolver().query(uri,null,null,null,null)) {
                    if(cursor!=null && cursor.moveToFirst()) {
                        int index=cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME);
                        if(index>=0) display=cursor.getString(index);
                    }
                }
                display=display.replaceAll("[\\\\/:*?\"<>|]","_");
                if(!display.toLowerCase().endsWith(".avatarbundle")) throw new Exception("请选择 .avatarbundle 文件");
                String name=display.substring(0,display.length()-13);
                if(name.length()>64) name=name.substring(0,64);
                output=new File(folder,name+"-"+UUID.randomUUID().toString().substring(0,8)+".avatarbundle");
                try(InputStream input=activity.getContentResolver().openInputStream(uri); FileOutputStream stream=new FileOutputStream(output)) {
                    if(input==null) throw new Exception("无法读取文件");
                    byte[] buffer=new byte[65536]; int count; long size=0;
                    while((count=input.read(buffer))!=-1) { size+=count; if(size>512L*1024*1024) throw new Exception("模型包超过 512 MB"); stream.write(buffer,0,count); }
                }
                reply(output.getAbsolutePath());
            } catch(Exception error) { if(output!=null) output.delete(); reply("ERROR:"+error.getMessage()); }
            activity.runOnUiThread(this::finish);
        },"AvatarImport").start();
    }
    void reply(String message) { UnityPlayer.UnitySendMessage(getArguments().getString("receiver"),getArguments().getString("method"),message); }
    void finish() { if(getActivity()!=null) getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); }
}
