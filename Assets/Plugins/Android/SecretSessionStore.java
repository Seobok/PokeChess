package com.pokechess.security;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

// SharedPreferences contains only versioned AES-GCM ciphertext. The AES key is non-exportable.
public final class SecretSessionStore {
    private static final String PREFS = "pokechess_secure_sessions_v1";
    private static SharedPreferences prefs(Context context) { return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE); }
    private static KeyStore keyStore() throws Exception { KeyStore store=KeyStore.getInstance("AndroidKeyStore"); store.load(null); return store; }
    private static SecretKey key(String scope, boolean create) throws Exception {
        KeyStore store=keyStore();
        if(store.containsAlias(scope)) return (SecretKey)store.getKey(scope,null);
        if(!create) throw new IllegalStateException("Session key unavailable");
        KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(scope,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256).setRandomizedEncryptionRequired(true).build());
        return generator.generateKey();
    }
    public static synchronized String read(Context context,String scope) throws Exception {
        String value=prefs(context).getString(scope,null); if(value==null) return null;
        String[] parts=value.split(":",-1); if(parts.length!=3||!parts[0].equals("v1")) throw new IllegalStateException("Invalid stored session format");
        byte[] iv=Base64.decode(parts[1],Base64.NO_WRAP); if(iv.length!=12) throw new IllegalStateException("Invalid IV");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE,key(scope,false),new GCMParameterSpec(128,iv));
        cipher.updateAAD(scope.getBytes(StandardCharsets.UTF_8));
        byte[] plaintext=cipher.doFinal(Base64.decode(parts[2],Base64.NO_WRAP));
        try { return new String(plaintext,StandardCharsets.US_ASCII); } finally { java.util.Arrays.fill(plaintext,(byte)0); }
    }
    public static synchronized boolean write(Context context,String scope,String token) throws Exception {
        if(!token.matches("[0-9A-Fa-f]{64}")) throw new IllegalArgumentException("Invalid session token");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE,key(scope,true));
        cipher.updateAAD(scope.getBytes(StandardCharsets.UTF_8));
        byte[] plaintext=token.getBytes(StandardCharsets.US_ASCII);
        try {
            byte[] encrypted=cipher.doFinal(plaintext);
            String value="v1:"+Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(encrypted,Base64.NO_WRAP);
            return prefs(context).edit().putString(scope,value).commit();
        } finally { java.util.Arrays.fill(plaintext,(byte)0); }
    }
    public static synchronized boolean delete(Context context,String scope) throws Exception {
        if(!prefs(context).edit().remove(scope).commit()) return false;
        KeyStore store=keyStore(); if(store.containsAlias(scope)) store.deleteEntry(scope); return true;
    }
    private SecretSessionStore() {}
}
