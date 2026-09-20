import React from 'react';
import { StatusBar, StyleSheet, Text, View } from 'react-native';

export default function App() {
  return (
    <View style={styles.screen}>
      <StatusBar barStyle="dark-content" />
      <Text style={styles.title}>AI Companion</Text>
      <Text style={styles.description}>你的随身伙伴，正在准备中。</Text>
      <Text style={styles.note}>开发预览 · 语音与角色将在后续版本接入</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, justifyContent: 'center', padding: 32, backgroundColor: '#f5f4ee' },
  title: { fontSize: 32, fontWeight: '600', color: '#1d3141', marginBottom: 16 },
  description: { fontSize: 18, color: '#496252', lineHeight: 28 },
  note: { fontSize: 13, color: '#62716c', marginTop: 24, lineHeight: 22 },
});
